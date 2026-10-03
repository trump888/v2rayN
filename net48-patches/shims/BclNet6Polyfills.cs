// =============================================================================
// BclNet6Polyfills.cs  —  NET48 PORT shim
// =============================================================================
// Additive polyfills for BCL surface introduced after .NET Framework 4.8 that
// upstream v2rayN calls into. Everything here is either a new type in a
// namespace net48 does not populate, or an extension method on an existing
// type. Both are additive, so upstream source compiles unmodified and a future
// rebase onto a newer release only has to delete what it stopped using.
//
// Groups, roughly in the order the compiler asks for them:
//   * PeriodicTimer             (.NET 6)
//   * Parallel.ForEachAsync     (.NET 6)  -> ParallelPolyfills (call-site rewrite)
//   * LINQ MaxBy/MinBy/DistinctBy/Chunk (.NET 6)
//   * Enum.GetNames<T> etc.     (.NET 7)  -> EnumGenericPolyfills (call-site rewrite)
//   * HttpClient/HttpContent CancellationToken overloads (.NET 5) -> BclPolyfills3.cs
//   * TcpListener.AcceptTcpClientAsync(CancellationToken) (.NET 6) -> BclPolyfills3.cs
//   * Dns.GetHostEntryAsync(host, ct) (.NET 8) -> call-site rewrite
//   * DecompressionMethods.All  (.NET 6)  -> call-site rewrite
//   * ReadOnlySpan<char> helpers (.NET 8)
//   * MemoryExtensions.IsEmpty  (.NET 10) -> C# 14 extension property
//
// Resource notes: none of these allocate eagerly or keep background threads
// alive at startup. PeriodicTimer uses a lazily created System.Threading.Timer,
// and Parallel.ForEachAsync runs on the existing thread pool. Everything else is
// straight-line managed code.
// =============================================================================

#if !NET5_0_OR_GREATER

using System.Globalization;

namespace System.Threading
{
    /// <summary>
    /// .NET 6 PeriodicTimer, shimmed on a Timer.
    /// </summary>
    internal sealed class PeriodicTimer : IDisposable
    {
        private readonly TimeSpan _period;
        private readonly Timer _timer;
        private readonly SemaphoreSlim _signal = new(0);
        private readonly CancellationTokenSource _disposed = new();
        private int _disposedFlag;

        public PeriodicTimer(TimeSpan period)
        {
            if (period <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(period));
            }
            _period = period;
            _timer = new Timer(OnTick, null, period, period);
        }

        public TimeSpan Period => _period;

        /// <summary>
        /// Waits for the next tick. Returns false if the timer was disposed
        /// while waiting.
        /// </summary>
        public async Task<bool> WaitForNextTickAsync(CancellationToken cancellationToken = default)
        {
            if (_disposedFlag != 0)
            {
                return false;
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _disposed.Token);
            try
            {
                await _signal.WaitAsync(linked.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private void OnTick(object? state)
        {
            if (_disposedFlag != 0 || _disposed.IsCancellationRequested)
            {
                return;
            }
            // Never block the timer callback on a full semaphore: a pending tick
            // is equivalent to a tick we already coalesced.
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // coalesce
            }
            catch (ObjectDisposedException)
            {
                // disposed concurrently
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposedFlag, 1) != 0)
            {
                return;
            }
            _timer.Dispose();
            _disposed.Cancel();
            _disposed.Dispose();
            _signal.Dispose();
        }
    }
}

namespace System.Threading.Tasks
{
    /// <summary>
    /// .NET 6 Parallel.ForEachAsync overloads. System.Threading.Tasks.Parallel
    /// is a static class and cannot host extension methods, so call sites are
    /// rewritten by rewrite-source.ps1:
    ///     Parallel.ForEachAsync(  ->  ParallelPolyfills.ForEachAsync(
    /// Both upstream overload shapes are provided: the ParallelOptions-first form
    /// that v2rayN actually uses, and the options-less form.
    /// </summary>
    internal static class ParallelPolyfills
    {
        public static Task ForEachAsync<TSource>(
            IEnumerable<TSource> source,
            ParallelOptions parallelOptions,
            Func<TSource, CancellationToken, ValueTask> body,
            CancellationToken cancellationToken = default)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (body is null) throw new ArgumentNullException(nameof(body));
            parallelOptions ??= new ParallelOptions();

            var items = source as IList<TSource> ?? source.ToList();
            if (items.Count == 0)
            {
                return Task.CompletedTask;
            }

            var effective = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, parallelOptions.CancellationToken);
            var effectiveOptions = new ParallelOptions
            {
                CancellationToken = effective.Token,
                MaxDegreeOfParallelism = parallelOptions.MaxDegreeOfParallelism,
                TaskScheduler = parallelOptions.TaskScheduler,
            };

            // Track completion separately from the delegate: an async body
            // marshalled through GetAwaiter().GetResult() would otherwise let
            // Parallel.ForEach return before the work is actually done.
            var countdown = new CountdownEvent(items.Count);
            var firstError = new AtomicErrorHolder();

            Parallel.ForEach(
                items,
                effectiveOptions,
                item =>
                {
                    try
                    {
                        body(item, effective.Token).AsTask().GetAwaiter().GetResult();
                    }
                    catch (OperationCanceledException) when (effective.IsCancellationRequested)
                    {
                        // Cooperative cancellation, not an error.
                    }
                    catch (Exception ex)
                    {
                        firstError.Set(ex);
                        effective.Cancel();
                    }
                    finally
                    {
                        countdown.Signal();
                    }
                });

            countdown.Wait();
            effective.Dispose();

            var error = firstError.Get();
            if (error is not null)
            {
                throw new AggregateException(error);
            }
            cancellationToken.ThrowIfCancellationRequested();
            parallelOptions.CancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public static Task ForEachAsync<TSource>(
            IEnumerable<TSource> source,
            Func<TSource, CancellationToken, ValueTask> body,
            CancellationToken cancellationToken = default)
            => ForEachAsync(source, new ParallelOptions(), body, cancellationToken);

        private sealed class AtomicErrorHolder
        {
            private Exception? _error;
            public void Set(Exception e) => Interlocked.CompareExchange(ref _error, e, null);
            public Exception? Get() => Volatile.Read(ref _error);
        }
    }
}

namespace System.Linq
{
    /// <summary>
    /// .NET 6+ LINQ operators missing from net48's System.Core.
    /// </summary>
    internal static class LinqNet6Polyfills
    {
        public static TSource? MaxBy<TSource, TKey>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
            where TKey : notnull
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));

            TSource? best = default;
            TKey? bestKey = default;
            var any = false;
            foreach (var item in source)
            {
                var key = keySelector(item);
                // Strictly greater keeps the first maximum, matching .NET.
                if (!any || Comparer<TKey>.Default.Compare(key, bestKey!) > 0)
                {
                    best = item;
                    bestKey = key;
                    any = true;
                }
            }
            return any ? best : default;
        }

        public static TSource? MaxBy<TSource, TKey>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            IComparer<TKey>? comparer)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            comparer ??= Comparer<TKey>.Default;

            TSource? best = default;
            TKey? bestKey = default;
            var any = false;
            foreach (var item in source)
            {
                var key = keySelector(item);
                if (!any || comparer.Compare(key, bestKey!) > 0)
                {
                    best = item;
                    bestKey = key;
                    any = true;
                }
            }
            return any ? best : default;
        }

        public static TSource? MinBy<TSource, TKey>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
            where TKey : notnull
            => MaxBy(source, keySelector, Comparer<TKey>.Default.Reverse());

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            return DistinctBy(source, keySelector, EqualityComparer<TKey>.Default);
        }

        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(
            this IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            IEqualityComparer<TKey> comparer)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (keySelector is null) throw new ArgumentNullException(nameof(keySelector));
            return DistinctByIterator(source, keySelector, comparer ?? EqualityComparer<TKey>.Default);
        }

        private static IEnumerable<TSource> DistinctByIterator<TSource, TKey>(
            IEnumerable<TSource> source,
            Func<TSource, TKey> keySelector,
            IEqualityComparer<TKey> comparer)
        {
            var seen = new HashSet<TKey>(comparer);
            foreach (var item in source)
            {
                if (seen.Add(keySelector(item)))
                {
                    yield return item;
                }
            }
        }

        public static IEnumerable<T[]> Chunk<T>(this IEnumerable<T> source, int size)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            if (size < 1) throw new ArgumentOutOfRangeException(nameof(size));

            // Array-backed buffer: avoids a List<T> allocation per chunk.
            var buffer = new T[size];
            var count = 0;
            foreach (var item in source)
            {
                buffer[count++] = item;
                if (count == size)
                {
                    yield return buffer;
                    buffer = new T[size];
                    count = 0;
                }
            }
            if (count > 0)
            {
                var last = new T[count];
                Array.Copy(buffer, last, count);
                yield return last;
            }
        }
    }

    /// <summary>
    /// Comparer direction flip, used by MinBy.
    /// </summary>
    internal sealed class ReverseComparer<T> : IComparer<T>
    {
        private readonly IComparer<T> _inner;
        public ReverseComparer(IComparer<T> inner) => _inner = inner;
        public int Compare(T? x, T? y) => _inner.Compare(y!, x!);
    }

    internal static class ComparerExtensions
    {
        public static IComparer<T> Reverse<T>(this IComparer<T> comparer)
            => new ReverseComparer<T>(comparer ?? Comparer<T>.Default);
    }
}

namespace System
{
    /// <summary>
    /// .NET 7 generic Enum reflection helpers. System.Enum is a static class and
    /// cannot host extension methods, so call sites are rewritten by
    /// rewrite-source.ps1:
    ///     Enum.GetNames&lt;T&gt;()   ->  EnumGenericPolyfills.GetNames&lt;T&gt;()
    /// </summary>
    internal static class EnumGenericPolyfills
    {
        public static string[] GetNames<TEnum>() where TEnum : struct, Enum
            => Enum.GetNames(typeof(TEnum));

        public static TEnum[] GetValues<TEnum>() where TEnum : struct, Enum
            => (TEnum[])Enum.GetValues(typeof(TEnum));

        public static string GetName<TEnum>(TEnum value) where TEnum : struct, Enum
            => Enum.GetName(typeof(TEnum), value) ?? string.Empty;

        public static string GetFormat<TEnum>(TEnum value) where TEnum : struct, Enum
            => value.ToString();

        public static bool IsDefined<TEnum>(TEnum value) where TEnum : struct, Enum
            => Enum.IsDefined(typeof(TEnum), value);
    }

    /// <summary>
    /// .NET 8 ReadOnlySpan&lt;char&gt; helpers. System.Memory 4.6.x predates them,
    /// so they are supplied here as extension methods. Extension methods do not
    /// take precedence over instance methods, so these only fill genuine gaps.
    /// </summary>
    internal static class SpanCharNet8Polyfills
    {
        /// <summary>
        /// .NET 8 ReadOnlySpan&lt;T&gt;.TryParse for the numeric types.
        /// System.UInt64 is a value type in mscorlib and cannot be extended, so
        /// call sites like `ulong.TryParse(span, out n)` are rewritten to
        /// SpanCharNet8Polyfills.TryParseUInt64 by rewrite-source.ps1.
        /// </summary>
        public static bool TryParseUInt64(this ReadOnlySpan<char> span, out ulong result)
            => ulong.TryParse(span.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out result);

        public static bool TryParseInt64(this ReadOnlySpan<char> span, out long result)
            => long.TryParse(span.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out result);

        /// <summary>
        /// .NET 8 MemoryExtensions.EnumerateLines.
        /// </summary>
        public static IEnumerable<string> EnumerateLines(this ReadOnlySpan<char> source)
        {
            // Materialise the span once; a ref struct cannot live in an iterator.
            return EnumerateLinesIterator(source.ToString());
        }

        /// <summary>
        /// .NET 8 MemoryExtensions.EnumerateLines, for a string receiver.
        /// Extension-method lookup does not consider the implicit
        /// string-to-ReadOnlySpan&lt;char&gt; conversion, so string needs its own.
        /// </summary>
        public static IEnumerable<string> EnumerateLines(this string source)
            => EnumerateLinesIterator(source ?? string.Empty);

        private static IEnumerable<string> EnumerateLinesIterator(string text)
        {
            using var reader = new StringReader(text);
            while (reader.ReadLine() is { } line)
            {
                yield return line;
            }
        }

        /// <summary>
        /// .NET 5 string.IsEmpty(), as a method for callers that use parens.
        /// </summary>
        public static bool IsEmptyMethod(this string value) => string.IsNullOrEmpty(value);

        /// <summary>
        /// .NET 8 MemoryExtensions.Contains for ReadOnlySpan&lt;char&gt;.
        /// </summary>
        public static bool Contains(this ReadOnlySpan<char> source, char value)
            => source.IndexOf(value) >= 0;

        public static bool Contains(this ReadOnlySpan<char> source, char value, StringComparison comparison)
            => source.IndexOf(value.ToString(), comparison) >= 0;

        public static bool Contains(this ReadOnlySpan<char> source, string value)
            => source.IndexOf(value.AsSpan(), StringComparison.Ordinal) >= 0;

        public static bool Contains(this ReadOnlySpan<char> source, string value, StringComparison comparison)
            => source.IndexOf(value.AsSpan(), comparison) >= 0;

        /// <summary>
        /// .NET 8 MemoryExtensions.Split for ReadOnlySpan&lt;char&gt;. The enumerator
        /// yields the bounds of each segment, matching the .NET 8 contract where
        /// consumers slice their own span with it.
        /// </summary>
        public static SpanSplitEnumerator Split(
            this ReadOnlySpan<char> source,
            char separator,
            StringSplitOptions options = StringSplitOptions.None)
        {
            // Materialise into a buffer the enumerator can hand out bounds for.
            var buffer = source.ToArray();
            return new SpanSplitEnumerator(buffer, separator, options);
        }

        /// <summary>
        /// .NET 8 MemoryExtensions.SplitToEnumerable.
        /// </summary>
        public static IEnumerable<string> SplitToEnumerable(
            this ReadOnlySpan<char> source,
            char separator,
            StringSplitOptions options = StringSplitOptions.None)
        {
            var buffer = source.ToArray();
            return SplitToEnumerableIterator(buffer, separator, options);
        }

        /// <summary>
        /// .NET 8 MemoryExtensions.SequenceCompareTo.
        /// </summary>
        public static int SequenceCompareTo(this ReadOnlySpan<char> span, ReadOnlySpan<char> other)
            => span.ToString().CompareTo(other.ToString(), StringComparison.Ordinal);

        private static IEnumerable<string> SplitToEnumerableIterator(
            char[] buffer,
            char separator,
            StringSplitOptions options)
        {
            var start = 0;
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != separator)
                {
                    continue;
                }
                var part = new string(buffer, start, i - start);
                start = i + 1;
                if (options == StringSplitOptions.RemoveEmptyEntries && part.Length == 0)
                {
                    continue;
                }
                yield return part;
            }
            var tail = new string(buffer, start, buffer.Length - start);
            if (options != StringSplitOptions.RemoveEmptyEntries || tail.Length != 0)
            {
                yield return tail;
            }
        }
    }

    /// <summary>
    /// Splits a char buffer, matching the shape of the .NET 8
    /// System.SpanSplitEnumerator (Current is a Range the consumer slices with).
    /// </summary>
    internal ref struct SpanSplitEnumerator
    {
        private readonly char[] _buffer;
        private readonly char _separator;
        private readonly StringSplitOptions _options;
        private int _start;
        private int _index;

        internal SpanSplitEnumerator(char[] buffer, char separator, StringSplitOptions options)
        {
            _buffer = buffer;
            _separator = separator;
            _options = options;
            _start = 0;
            _index = 0;
            Current = Range.All;
        }

        public SpanSplitEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            while (_index < _buffer.Length)
            {
                if (_buffer[_index] != _separator)
                {
                    _index++;
                    continue;
                }
                var part = new Range(_start, _index);
                _start = ++_index;
                if (_options == StringSplitOptions.RemoveEmptyEntries &&
                    part.End.Value - part.Start.Value == 0)
                {
                    continue;
                }
                Current = part;
                return true;
            }

            if (_start > _buffer.Length)
            {
                return false;
            }

            var tail = new Range(_start, _buffer.Length);
            _start = _buffer.Length + 1;
            if (_options == StringSplitOptions.RemoveEmptyEntries &&
                tail.End.Value - tail.Start.Value == 0)
            {
                return false;
            }
            Current = tail;
            return true;
        }

        /// <summary>
        /// Bounds of the current segment. Consumers slice their own span with it,
        /// which is how the upstream code reads `leftSpan[leftEnum.Current]`.
        /// </summary>
        public Range Current { get; private set; }

        /// <summary>
        /// Present so `using var` over this ref struct is accepted. A ref struct
        /// cannot implement IDisposable, but the compiler's using-statement
        /// pattern only needs a callable Dispose. This enumerator owns no
        /// unmanaged or disposable resource, so there is nothing to release.
        /// </summary>
        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Holder for the C# 14 extension properties below. C# 14 requires an
    /// `extension` block to live in a top-level, non-generic, static class, so
    /// these cannot simply be added to SpanCharNet8Polyfills's method list.
    /// </summary>
    internal static class SpanExtensions
    {
        /// <summary>
        /// .NET 10 exposes MemoryExtensions.IsEmpty as an extension *property*,
        /// which upstream reads as `span.IsEmpty` (no parentheses).
        ///
        /// This must stay a property rather than becoming a method: the upstream
        /// call sites omit parentheses, so a method would bind as a method group
        /// and break `if (span.IsEmpty || ...)`.
        ///
        /// Needs LangVersion=preview (C# 14), which Directory.Build.props sets.
        /// </summary>
        extension(ReadOnlySpan<char> span)
        {
            public bool IsEmpty => span.Length == 0;
        }

        extension(ReadOnlyMemory<char> memory)
        {
            public bool IsEmpty => memory.Length == 0;
        }
    }
}

#endif