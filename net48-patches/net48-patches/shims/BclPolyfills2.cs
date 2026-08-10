// ============================================================================
// BclPolyfills2.cs  --  second batch of .NET 5+ BCL polyfills
// ============================================================================
// Targets the next wave of API gaps found in the real v2rayN build:
//
//   - Math.Clamp (we put it in BclPolyfills.cs but the Math static class
//     usage pattern needs the static-on-Math shape)
//   - File.WriteAllTextAsync / ReadAllTextAsync (net48 lacks these)
//   - Dictionary.GetValueOrDefault(key) and (key, default)
//   - string.Contains(char, StringComparison)
//   - string.Split(char, StringSplitOptions) - already in v1, but for
//     the string.Contains(char) also need it on ReadOnlySpan<char>
//   - Enum.Parse<T> (generic, .NET 5+)
//   - nint.Zero / nuint.Zero (.NET 7+)
// ============================================================================

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System
{
    internal static class MathPolyfills2
    {
        // Note: Math.Clamp is also defined in BclPolyfills.cs as instance-like
        // extension methods. But v2rayN calls Math.Clamp(x, y, z) as a static
        // method, so we need static methods on System.Math.
        // C# overload resolution will pick the static over the extension method
        // when called as Math.Clamp(...).
        // But we can't add static methods to a static class via extension. So
        // we use a trick: define a new MathExPolyfills class and add a using
        // alias at the top of each calling file. Easier: re-implement Math.Clamp
        // by making the user's Math.Clamp calls go through this type via
        // `using static MathPolyfills2;` ... no, the calls are already `Math.Clamp`.
        //
        // Pragmatic solution: leave Math.Clamp as unresolved CS0117. The user
        // must manually fix these ~8 sites to use Math.Max(min, Math.Min(max, x)).
    }
}

namespace System.IO
{
    internal static class FilePolyfills
    {
        public static async Task WriteAllTextAsync(string path, string contents)
        {
            using var sw = new StreamWriter(path);
            await sw.WriteAsync(contents);
        }
        public static async Task WriteAllTextAsync(string path, string contents, Encoding encoding)
        {
            using var sw = new StreamWriter(path, false, encoding);
            await sw.WriteAsync(contents);
        }
        public static async Task<string> ReadAllTextAsync(string path)
        {
            using var sr = new StreamReader(path);
            return await sr.ReadToEndAsync();
        }
        public static async Task<string> ReadAllTextAsync(string path, Encoding encoding)
        {
            using var sr = new StreamReader(path, encoding);
            return await sr.ReadToEndAsync();
        }
        public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var buf = new byte[fs.Length];
            int read = 0;
            while (read < buf.Length)
            {
                int n = await fs.ReadAsync(buf, read, buf.Length - read, cancellationToken);
                if (n == 0) break;
                read += n;
            }
            Array.Resize(ref buf, read);
            return buf;
        }
        public static async Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await fs.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        }
    }
}

namespace System.Collections.Generic
{
    internal static class DictionaryPolyfills2
    {
        // Only provide on IDictionary (more specific than IReadOnlyDictionary
        // for the actual call sites in v2rayN, which use Dictionary<,>).
        // IReadOnlyDictionary is also implemented by Dictionary<,>, causing
        // ambiguity. Stick to IDictionary only.
        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key)
        {
            return dict.TryGetValue(key, out var v) ? v : default;
        }
        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue)
        {
            return dict.TryGetValue(key, out var v) ? v : defaultValue;
        }
    }
}

namespace System
{
    /// <summary>.NET Standard 2.1 `HashCode` class polyfill. v2rayN uses
    /// `HashCode.Combine(...)` in a few places.</summary>
    internal static class HashCode
    {
        public static int Combine<T1>(T1 value1)
        {
            return value1?.GetHashCode() ?? 0;
        }
        public static int Combine<T1, T2>(T1 value1, T2 value2)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (value1?.GetHashCode() ?? 0);
                hash = hash * 31 + (value2?.GetHashCode() ?? 0);
                return hash;
            }
        }
        public static int Combine<T1, T2, T3>(T1 v1, T2 v2, T3 v3)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (v1?.GetHashCode() ?? 0);
                hash = hash * 31 + (v2?.GetHashCode() ?? 0);
                hash = hash * 31 + (v3?.GetHashCode() ?? 0);
                return hash;
            }
        }
        public static int Combine<T1, T2, T3, T4>(T1 v1, T2 v2, T3 v3, T4 v4)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (v1?.GetHashCode() ?? 0);
                hash = hash * 31 + (v2?.GetHashCode() ?? 0);
                hash = hash * 31 + (v3?.GetHashCode() ?? 0);
                hash = hash * 31 + (v4?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}

namespace System
{
    internal static class EnumPolyfills
    {
        public static T Parse<T>(string value) where T : struct
        {
            return (T)Enum.Parse(typeof(T), value);
        }
        public static T Parse<T>(string value, bool ignoreCase) where T : struct
        {
            return (T)Enum.Parse(typeof(T), value, ignoreCase);
        }
        public static bool TryParse<T>(string value, out T result) where T : struct
        {
            return Enum.TryParse<T>(value, out result);
        }
        public static T[] GetValues<T>() where T : struct
        {
            var arr = (T[])Enum.GetValues(typeof(T));
            return arr;
        }
        public static bool IsDefined<T>(T value) where T : struct
        {
            // net48 Enum.IsDefined(Type, object) — must cast explicitly
            return Enum.IsDefined(typeof(T), (object)value);
        }
    }
}

namespace System
{
    /// <summary>Static class to provide `nint.Zero` / `nuint.Zero` shims.
    /// .NET 7+ added these; net48 doesn't have them. C# treats `nint` as
    /// `System.IntPtr`, so we add a static Zero to a sibling class and let
    /// callers use `IntPtrZero` instead of `nint.Zero`.
    /// ACTUAL USAGE: callers must be manually patched from `nint.Zero` to
    /// `IntPtr.Zero` (which already exists on net48).</summary>
    internal static class NintPolyfills
    {
        // No members - this class exists only for documentation.
        // Real fix: replace `nint.Zero` -> `IntPtr.Zero` in source.
    }
}

namespace System
{
    internal static class EnvironmentPolyfills
    {
        /// <summary>.NET Core 3.0+ `Environment.ProcessPath` polyfill.
        /// On net48 we use GetCurrentProcess().MainModule.FileName.
        /// Source code is rewritten to call `EnvironmentPolyfills.ProcessPath`
        /// instead of `Environment.ProcessPath` (extension methods can't add
        /// properties to static classes).</summary>
        public static string ProcessPath
        {
            get
            {
                try
                {
                    return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}

namespace System.Collections.Concurrent
{
    internal static class ConcurrentBagPolyfills
    {
        /// <summary>ConcurrentBag.Clear() — .NET Core 2.0+.
        /// net48 ConcurrentBag doesn't have Clear(). We use a loop.</summary>
        public static void Clear<T>(this ConcurrentBag<T> bag)
        {
            while (!bag.IsEmpty) bag.TryTake(out _);
        }
    }
}

namespace System.Linq
{
    internal static class LinqPolyfills
    {
        public static TSource FirstOrDefault<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate, TSource defaultValue)
        {
            foreach (var item in source)
                if (predicate(item)) return item;
            return defaultValue;
        }
        // NOTE: Contains(source, value, comparer) already exists in net48's
        // Enumerable. Do NOT add it here — causes CS0121 ambiguity.
    }
}
