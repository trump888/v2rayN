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
        public static bool Contains<TSource>(this IEnumerable<TSource> source, TSource value, IEqualityComparer<TSource> comparer)
        {
            if (comparer == null) return source.Contains(value);
            foreach (var item in source)
                if (comparer.Equals(item, value)) return true;
            return false;
        }
    }
}
