// ============================================================================
// BclPolyfills.cs  --  .NET 5+ BCL API polyfills for .NET Framework 4.8
// ============================================================================
// This file shims the most commonly used .NET 5+ / .NET 6+ / .NET 7+ BCL
// APIs that v2rayN 7.24.4 relies on heavily:
//
//   - System.Range / System.Index          (used by [..n] slicing)
//   - StringSplitOptions.TrimEntries       (used in lots of split calls)
//   - MD5.HashData / SHA256.HashData       (used by hash helpers)
//   - Math.Clamp                           (used by config validation)
//   - ArgumentNullException.ThrowIfNull    (used by ctor guards)
//   - ArgumentException.ThrowIfNullOrEmpty (used by string guards)
//   - Dictionary.TryAdd                    (extension method on net48)
//   - string.Contains(char)                (net48 only has string.Contains(string))
//   - string.Split(char, options)          (net48 only has string.Split(char[]) / string.Split(string[]))
//   - Task.WaitAsync(CancellationToken)    (used by call sites that cancel)
//   - X509Certificate2.CreateFromPem       (used by CertPemManager)
//   - X509ChainPolicy.TrustMode/CustomTrustStore (used by CertPemManager)
//
// Where the API surface is large (Range/Index), we provide the full type
// with operator support, copied from the .NET Runtime reference impl.
//
// Where the API is a single static method (MD5.HashData etc.), we provide
// extension-method-shaped static helpers using the same qualified name.
// ============================================================================

#pragma warning disable CS0436 // Type conflicts with imported type (intentional)

using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

// ---------------------------------------------------------------------------
// System.Index / System.Range  (net48 polyfill)
// ---------------------------------------------------------------------------

namespace System
{
    /// <summary>.NET Core 3.0+ `System.Index` polyfill for net48.</summary>
    public readonly struct Index : IEquatable<Index>
    {
        private readonly int _value;
        public Index(int value, bool fromEnd = false)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
            _value = fromEnd ? ~value : value;
        }
        public int Value => _value < 0 ? ~_value : _value;
        public bool IsFromEnd => _value < 0;
        public int GetOffset(int length)
        {
            int n = _value;
            return n < 0 ? n + length : n;
        }
        public bool Equals(Index other) => _value == other._value;
        public override bool Equals(object value) => value is Index i && Equals(i);
        public override int GetHashCode() => _value;
        public override string ToString() => IsFromEnd ? "^" + Value : Value.ToString();
        public static Index Start => new Index(0);
        public static Index End => new Index(~0);
        public static Index FromStart(int value) => new Index(value);
        public static Index FromEnd(int value) => new Index(value, fromEnd: true);
        public static implicit operator Index(int value) => FromStart(value);
    }

    /// <summary>.NET Core 3.0+ `System.Range` polyfill for net48.</summary>
    public readonly struct Range : IEquatable<Range>
    {
        public Index Start { get; }
        public Index End { get; }
        public Range(Index start, Index end) { Start = start; End = end; }
        public bool Equals(Range other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object value) => value is Range r && Equals(r);
        public override int GetHashCode() => Start.GetHashCode() ^ End.GetHashCode();
        public override string ToString() => Start + ".." + End;
        public static Range StartAt(Index start) => new Range(start, Index.End);
        public static Range EndAt(Index end) => new Range(Index.Start, end);
        public static Range All => new Range(Index.Start, Index.End);
        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int start = Start.GetOffset(length);
            int end = End.GetOffset(length);
            if ((uint)end > (uint)length || (uint)start > (uint)end)
                throw new ArgumentOutOfRangeException(nameof(length));
            return (start, end - start);
        }
    }

    // Extension methods for [Range] indexing on common types
    internal static class RangeExtensions
    {
        public static string Slice(this string s, Range r)
        {
            var (off, len) = r.GetOffsetAndLength(s.Length);
            return s.Substring(off, len);
        }
        public static T[] Slice<T>(this T[] arr, Range r)
        {
            var (off, len) = r.GetOffsetAndLength(arr.Length);
            var result = new T[len];
            Array.Copy(arr, off, result, 0, len);
            return result;
        }
        public static Span<T> Slice<T>(this Span<T> span, Range r)
        {
            var (off, len) = r.GetOffsetAndLength(span.Length);
            return span.Slice(off, len);
        }
        public static ReadOnlySpan<T> Slice<T>(this ReadOnlySpan<T> span, Range r)
        {
            var (off, len) = r.GetOffsetAndLength(span.Length);
            return span.Slice(off, len);
        }
        public static List<T> Slice<T>(this List<T> list, Range r)
        {
            var (off, len) = r.GetOffsetAndLength(list.Count);
            return list.GetRange(off, len);
        }
    }
}

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// .NET Core 3.0+ provides `RuntimeHelpers.GetSubArray&lt;T&gt;(T[], Range)`
    /// which the C# compiler emits when you write `array[1..n]`.
    /// .NET Framework 4.8 does NOT ship this method, so we polyfill it.
    /// </summary>
    internal static class RuntimeHelpersPolyfills
    {
        public static T[] GetSubArray<T>(T[] array, Range range)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            var (offset, length) = range.GetOffsetAndLength(array.Length);
            if (default(T) == null && length != 0)
            {
                var result = new T[length];
                Array.Copy(array, offset, result, 0, length);
                return result;
            }
            else
            {
                var result = new T[length];
                Array.Copy(array, offset, result, 0, length);
                return result;
            }
        }
    }
}

// ---------------------------------------------------------------------------
// String / Char polyfills
// ---------------------------------------------------------------------------

namespace System
{
    internal static class StringPolyfills
    {
        public static bool Contains(this string s, char c) => s.IndexOf(c) >= 0;
        public static bool Contains(this string s, char c, StringComparison _)
            => s.IndexOf(c) >= 0;
        public static bool Contains(this string s, string value, StringComparison comparison)
            => s.IndexOf(value, comparison) >= 0;
        public static bool StartsWith(this string s, char c) => s.Length > 0 && s[0] == c;
        public static bool EndsWith(this string s, char c) => s.Length > 0 && s[s.Length - 1] == c;
        public static string Replace(this string s, char oldChar, char newChar)
            => s.Replace(oldChar.ToString(), newChar.ToString());
        public static string[] Split(this string s, char separator, StringSplitOptions options = StringSplitOptions.None)
            => s.Split(new[] { separator }, options);
        public static string[] Split(this string s, char separator, int count, StringSplitOptions options = StringSplitOptions.None)
            => s.Split(new[] { separator }, count, options);
        public static string[] Split(this string s, string separator, StringSplitOptions options = StringSplitOptions.None)
            => s.Split(new[] { separator }, options);

        // string.Chunk(int size) — .NET 6+ returns IEnumerable<ReadOnlyMemory<char>>
        // We return IEnumerable<string> for easier consumption.
        public static System.Collections.Generic.IEnumerable<string> Chunk(this string s, int chunkSize)
        {
            if (s == null) yield break;
            for (int i = 0; i < s.Length; i += chunkSize)
                yield return s.Substring(i, System.Math.Min(chunkSize, s.Length - i));
        }
    }
}

// ---------------------------------------------------------------------------
// StringSplitOptions.TrimEntries  (polyfill via enum extension)
// ---------------------------------------------------------------------------

namespace System
{
    internal static class StringSplitOptionsPolyfills
    {
        // .NET 5+ defines StringSplitOptions.TrimEntries == 2.
        // net48's StringSplitOptions enum only has None=0, RemoveEmptyEntries=1.
        // We can't add to an existing enum, so we expose the value as a const
        // and patch the call sites via an extension method.
        public const StringSplitOptions TrimEntries = (StringSplitOptions)2;
    }
}

// ---------------------------------------------------------------------------
// HashAlgorithm.HashData polyfills
// ---------------------------------------------------------------------------

namespace System.Security.Cryptography
{
    internal static class HashAlgorithmPolyfills
    {
        public static byte[] HashData(this MD5 _, byte[] source)
        {
            using var md5 = MD5.Create();
            return md5.ComputeHash(source);
        }
        public static byte[] HashData(this MD5 _, ReadOnlySpan<byte> source)
        {
            using var md5 = MD5.Create();
            return md5.ComputeHash(source.ToArray());
        }
        public static byte[] HashData(this SHA256 _, byte[] source)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(source);
        }
        public static byte[] HashData(this SHA256 _, ReadOnlySpan<byte> source)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(source.ToArray());
        }
        public static byte[] HashData(this SHA1 _, byte[] source)
        {
            using var sha = SHA1.Create();
            return sha.ComputeHash(source);
        }
    }
}

// ---------------------------------------------------------------------------
// Math.Clamp polyfill
// ---------------------------------------------------------------------------

namespace System
{
    internal static class MathPolyfills
    {
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
        public static long Clamp(long value, long min, long max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
        public static byte Clamp(byte value, byte min, byte max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}

// ---------------------------------------------------------------------------
// ArgumentNullException.ThrowIfNull / ArgumentException.ThrowIfNullOrEmpty
// ---------------------------------------------------------------------------

namespace System
{
    internal static class ArgumentPolyfills
    {
        public static void ThrowIfNull(object arg, [CallerArgumentExpression(nameof(arg))] string paramName = null)
        {
            if (arg == null) throw new ArgumentNullException(paramName);
        }
        public static void ThrowIfNullOrEmpty(string arg, [CallerArgumentExpression(nameof(arg))] string paramName = null)
        {
            if (string.IsNullOrEmpty(arg)) throw new ArgumentException("Value cannot be null or empty.", paramName);
        }
        public static void ThrowIfNullOrWhiteSpace(string arg, [CallerArgumentExpression(nameof(arg))] string paramName = null)
        {
            if (string.IsNullOrWhiteSpace(arg)) throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }
    }
}

// ---------------------------------------------------------------------------
// Dictionary.TryAdd  (net48 Dictionary lacks TryAdd)
// ---------------------------------------------------------------------------

namespace System.Collections.Generic
{
    internal static class DictionaryPolyfills
    {
        public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue value)
        {
            if (dict.ContainsKey(key)) return false;
            dict.Add(key, value);
            return true;
        }
    }
}

// ---------------------------------------------------------------------------
// X509Certificate2.CreateFromPem  (.NET 5+)
// ---------------------------------------------------------------------------

namespace System.Security.Cryptography.X509Certificates
{
    internal static class X509Certificate2Polyfills
    {
        /// <summary>Parses a PEM-encoded certificate into an X509Certificate2.</summary>
        public static X509Certificate2 CreateFromPem(string pem)
        {
            // Strip PEM headers/footers and decode base64 body.
            var lines = pem.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var b64 = new StringBuilder();
            foreach (var line in lines)
            {
                if (line.StartsWith("-----")) continue;
                b64.Append(line.Trim());
            }
            var bytes = Convert.FromBase64String(b64.ToString());
            return new X509Certificate2(bytes);
        }
    }
}

// ---------------------------------------------------------------------------
// X509ChainPolicy.TrustMode / CustomTrustStore (.NET 5+)
// ---------------------------------------------------------------------------
// net48 X509ChainPolicy doesn't have these. We provide no-op shims that let
// the code compile but ignore custom trust store configuration. Users who
// need custom root trust on net48 should configure it via Windows cert store.

namespace System.Security.Cryptography.X509Certificates
{
    internal enum X509ChainTrustMode { System, CustomRootTrust }

    internal static class X509ChainPolicyPolyfills
    {
        public static X509ChainTrustMode get_TrustMode(this X509ChainPolicy _) => X509ChainTrustMode.System;
        public static void set_TrustMode(this X509ChainPolicy _, X509ChainTrustMode value) { /* no-op */ }
        public static X509Certificate2Collection get_CustomTrustStore(this X509ChainPolicy _) => new X509Certificate2Collection();
        public static void set_CustomTrustStore(this X509ChainPolicy _, X509Certificate2Collection value) { /* no-op */ }
        // For statement form: chainPolicy.CustomTrustStore.AddRange(certs)
        // We can't make that work via extension; rewrite to AddToCustomTrustStore.
        public static void AddToCustomTrustStore(this X509ChainPolicy policy, X509Certificate2Collection certs)
        {
            // net48: no-op (system trust store only)
        }
    }
}

// ---------------------------------------------------------------------------
// Task.WaitAsync(CancellationToken)  (.NET 6+)
// ---------------------------------------------------------------------------

namespace System.Threading.Tasks
{
    internal static class TaskPolyfills
    {
        public static Task WaitAsync(this Task task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled) return task;
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled(cancellationToken);
            var tcs = new TaskCompletionSource<bool>();
            var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            task.ContinueWith(t =>
            {
                reg.Dispose();
                if (t.IsFaulted) tcs.TrySetException(t.Exception.InnerException);
                else if (t.IsCanceled) tcs.TrySetCanceled();
                else tcs.TrySetResult(true);
            });
            return tcs.Task;
        }
        public static Task<T> WaitAsync<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled) return task;
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<T>(cancellationToken);
            var tcs = new TaskCompletionSource<T>();
            var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            task.ContinueWith(t =>
            {
                reg.Dispose();
                if (t.IsFaulted) tcs.TrySetException(t.Exception.InnerException);
                else if (t.IsCanceled) tcs.TrySetCanceled();
                else tcs.TrySetResult(t.Result);
            });
            return tcs.Task;
        }
    }
}
