// =============================================================================
// Lock.cs  —  NET48 PORT shim
// =============================================================================
// System.Threading.Lock arrived in .NET 9. Upstream v2rayN (net10.0) uses the
// non-generic form as a reader/writer lock, e.g. in EventChannel and
// SpeedtestService:
//
//     private readonly Lock _gate = new();
//     _signal.Synchronize(_gate);
//
//     lock (_gate) { ... }
//
// Rather than rewriting upstream call sites, we supply the same type on net48.
//
// IMPORTANT: the member names and shapes here must match the BCL exactly, down
// to `public sealed class` and the nested `public readonly ref struct Scope`.
// When `LangVersion` is `preview`, the C# compiler special-cases a `lock`
// statement whose target is the well-known type System.Threading.Lock and
// lowers it to `EnterScope()`/`Dispose()` instead of Monitor.Enter/Exit. If the
// shape does not match, the build fails with:
//
//     error CS0656: Missing compiler required member
//                   'System.Threading.Lock.EnterScope'
//
// so do not "simplify" this file's shape without re-running verify-local.sh.
//
// Semantics below match .NET 9's Lock: reentrant for the owning thread, and
// writer-preferring so a steady stream of readers cannot starve a pending
// writer. Underneath we use ReaderWriterLockSlim, which gives both properties.
// =============================================================================

#if !NET5_0_OR_GREATER

namespace System.Threading;

/// <summary>
/// A lightweight reader/writer lock. Shims System.Threading.Lock on net48.
/// </summary>
public sealed class Lock
{
    private readonly ReaderWriterLockSlim _lock =
        new(LockRecursionPolicy.SupportsRecursion);

    public bool IsHeldByCurrentThread => _lock.IsReadLockHeld || _lock.IsWriteLockHeld;

    public void Enter() => _lock.EnterWriteLock();

    public void Exit() => _lock.ExitWriteLock();

    public Scope EnterScope()
    {
        _lock.EnterWriteLock();
        return new Scope(this, ScopeKind.Write);
    }

    public Scope EnterUpgradeableReadScope()
    {
        _lock.EnterUpgradeableReadLock();
        return new Scope(this, ScopeKind.UpgradeableRead);
    }

    public Scope EnterReadScope()
    {
        _lock.EnterReadLock();
        return new Scope(this, ScopeKind.Read);
    }

    internal enum ScopeKind
    {
        Read,
        UpgradeableRead,
        Write,
    }

    /// <summary>
    /// Scope returned by the Enter*Scope methods. Disposing releases the lock.
    /// Matches the BCL contract: Dispose is single-shot, and calling it twice on
    /// the same scope is a programming error (as it is on .NET 9).
    /// </summary>
    public readonly ref struct Scope
    {
        private readonly Lock _owner;
        private readonly ScopeKind _kind;

        internal Scope(Lock owner, ScopeKind kind)
        {
            _owner = owner;
            _kind = kind;
        }

        public void Dispose()
        {
            switch (_kind)
            {
                case ScopeKind.Read:
                    _owner._lock.ExitReadLock();
                    break;
                case ScopeKind.UpgradeableRead:
                    _owner._lock.ExitUpgradeableReadLock();
                    break;
                default:
                    _owner._lock.ExitWriteLock();
                    break;
            }
        }
    }
}

#endif