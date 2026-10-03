// ============================================================================
// WpfPolyfills.cs  --  WPF-specific polyfills for the net48 port
// ============================================================================
// History: this file used to shim three things that ReactiveUI 18.x /
// MaterialDesign 3.2.0 did not have. With the port now on ReactiveUI 24.x and
// MaterialDesignThemes 5.3.2 (the same versions upstream uses) all three are
// provided by the real packages:
//
//   * ReactiveUI.Builder.RxAppBuilder      -> ReactiveUI.WPF 24.2.0
//   * ITheme.SetBaseTheme(BaseTheme)       -> MaterialDesignThemes 5.3.2
//   * IDisposable.DisposeWith(set)         -> ReactiveUI 24.x
//
// Keeping local copies would be actively harmful: an `internal sealed class
// RxAppBuilder` in namespace ReactiveUI.Builder shadows the imported one, so
// the app would build but silently skip ReactiveUI's WPF platform registration
// (no scheduler, no activation mode) - a runtime failure, not a compile error.
//
// What remains is the DisposeWith shim. Upstream call sites are rewritten
// .DisposeWith( -> .__DisposeWith( by rewrite-source.ps1 so the extension binds
// against an unconstrained generic receiver; the BCL-side
// DisposableMixins.DisposeWith requires IDisposable, and several upstream call
// sites pass binding expressions that do not implement it.
//
// The collection parameter is typed as `object` on purpose. ReactiveUI 24
// renamed CompositeDisposable to DisposableSet and no longer ships the
// System.Reactive package, so hard-coding either name would break again on the
// next rebase. FindAdder() resolves the collector's Add method once and caches
// the lookup.
//
// Cost: one cached reflection lookup per distinct collector type, then a plain
// reflective invoke. DisposeWith is only reached during view activation, never
// on a hot path, so this does not affect startup time or steady-state memory.
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace v2rayN.Common
{
    internal static class DisposeWithExtensions
    {
        private static readonly ConcurrentDictionary<Type, MethodInfo?> _adders = new();

        /// <summary>
        /// Adds <paramref name="disposable"/> to <paramref name="disposables"/>
        /// and returns it unchanged, mirroring ReactiveUI's fluent DisposeWith.
        /// </summary>
        public static T __DisposeWith<T>(this T disposable, object? disposables)
        {
            if (disposable is IDisposable d && disposables is not null)
            {
                var add = _adders.GetOrAdd(disposables.GetType(), FindAdder);
                if (add is null)
                {
                    // Fail loudly rather than silently leaking: a forgotten
                    // Dispose here keeps event handlers bound to a closed
                    // ViewModel, which is far harder to diagnose later.
                    throw new NotSupportedException(
                        $"net48 port: '{disposables.GetType().FullName}' has no " +
                        "Add(IDisposable) method, so DisposeWith cannot register " +
                        "the disposable.");
                }

                add.Invoke(disposables, [d]);
            }
            return disposable;
        }

        private static MethodInfo? FindAdder(Type collectorType)
            => collectorType.GetMethod(
                "Add",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: [typeof(IDisposable)],
                modifiers: null);
    }
}