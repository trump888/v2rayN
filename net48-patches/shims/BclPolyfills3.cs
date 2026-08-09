// ============================================================================
// BclPolyfills3.cs  --  Third batch of .NET 5+ BCL polyfills (static classes)
// ============================================================================
// These APIs are STATIC methods/properties on existing static classes.
// Extension methods can't add them, so we either:
//   (a) provide a sibling class with the same name in our own namespace and
//       let source use `using static` — too invasive.
//   (b) REWRITE source calls to use a sibling class (e.g. OperatingSystem ->
//       OperatingSystemPolyfills). This is what we do.
//
// The rewriter (rewrite-source.ps1, Rewrite 15-25) handles the source-side
// rename. This file provides the implementations.
// ============================================================================

using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace System
{
    /// <summary>.NET 5+ `OperatingSystem` static class polyfill.
    /// Source code rewritten to call `OperatingSystemPolyfills.IsWindows()`
    /// instead of `OperatingSystem.IsWindows()`.</summary>
    internal static class OperatingSystemPolyfills
    {
        public static bool IsWindows() => Environment.OSVersion.Platform == PlatformID.Win32NT;
        public static bool IsLinux() => Environment.OSVersion.Platform == PlatformID.Unix;
        public static bool IsMacOS() => Environment.OSVersion.Platform == PlatformID.MacOSX;
        public static bool IsWindowsVersion(int major, int minor = 0, int build = 0, int revision = 0)
        {
            if (!IsWindows()) return false;
            var v = Environment.OSVersion.Version;
            if (v.Major != major) return v.Major > major;
            if (v.Minor != minor) return v.Minor > minor;
            if (v.Build != build) return v.Build > build;
            return v.Revision >= revision;
        }
    }

    /// <summary>.NET 5+ `ArgumentNullException.ThrowIfNull` polyfill.
    /// Source code rewritten to call `ArgumentNullExceptionPolyfills.ThrowIfNull`.</summary>
    internal static class ArgumentNullExceptionPolyfills
    {
        public static void ThrowIfNull(object arg, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(arg))] string paramName = null)
        {
            if (arg == null) throw new ArgumentNullException(paramName);
        }
    }

    /// <summary>.NET 5+ `ArgumentException.ThrowIfNullOrEmpty` polyfill.</summary>
    internal static class ArgumentExceptionPolyfills
    {
        public static void ThrowIfNullOrEmpty(string arg, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(arg))] string paramName = null)
        {
            if (string.IsNullOrEmpty(arg)) throw new ArgumentException("Value cannot be null or empty.", paramName);
        }
        public static void ThrowIfNullOrWhiteSpace(string arg, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(arg))] string paramName = null)
        {
            if (string.IsNullOrWhiteSpace(arg)) throw new ArgumentException("Value cannot be null or whitespace.", paramName);
        }
    }

    /// <summary>MD5.HashData etc. — instance method on a static class.
    /// We rewrite source `MD5.HashData(bytes)` -> `HashAlgorithmPolyfills.HashData(MD5.HashAlgorithm(), bytes)`
    /// — but simpler: just provide static helpers and rewrite source to call them directly.</summary>
    internal static class HashAlgorithmStaticPolyfills
    {
        public static byte[] MD5HashData(byte[] source)
        {
            using var md5 = MD5.Create();
            return md5.ComputeHash(source);
        }
        public static byte[] MD5HashData(System.ReadOnlySpan<byte> source)
        {
            using var md5 = MD5.Create();
            return md5.ComputeHash(source.ToArray());
        }
        public static byte[] SHA256HashData(byte[] source)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(source);
        }
        public static byte[] SHA256HashData(System.ReadOnlySpan<byte> source)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(source.ToArray());
        }
        public static byte[] SHA1HashData(byte[] source)
        {
            using var sha = SHA1.Create();
            return sha.ComputeHash(source);
        }
    }

    /// <summary>File.AppendAllTextAsync polyfill.</summary>
    internal static class FileAppendPolyfills
    {
        public static async Task AppendAllTextAsync(string path, string contents)
        {
            using var sw = new StreamWriter(path, append: true);
            await sw.WriteAsync(contents);
        }
        public static async Task AppendAllTextAsync(string path, string contents, Encoding encoding)
        {
            using var sw = new StreamWriter(path, append: true, encoding: encoding);
            await sw.WriteAsync(contents);
        }
        public static async Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken)
        {
            using var sw = new StreamWriter(path, append: true);
            await sw.WriteAsync(contents);
        }
    }

    /// <summary>CompressionLevel.SmallestSize polyfill (net6+).
    /// net48's CompressionLevel enum only has Optimal, NoCompression, Fastest.</summary>
    internal static class CompressionLevelPolyfills
    {
        public const CompressionLevel SmallestSize = CompressionLevel.Optimal;
    }

    /// <summary>StringSplitOptions.TrimEntries polyfill (net5+).
    /// net48's StringSplitOptions enum only has None, RemoveEmptyEntries.</summary>
    internal static class StringSplitOptionsPolyfillsStatic
    {
        public const StringSplitOptions TrimEntries = (StringSplitOptions)2;
    }
}

namespace System.Net.Http.Headers
{
    /// <summary>MediaTypeNames.Application.Json polyfill (net5+).</summary>
    internal static class MediaTypeNamesApplicationPolyfills
    {
        public const string Json = "application/json";
        public const string Xml = "application/xml";
        public const string Text = "text/plain";
    }
}

namespace System.Runtime.InteropServices
{
    /// <summary>Marshal.GetLastPInvokeError polyfill — already exists in net48
    /// but under a different name. We expose the new name as a redirect.</summary>
    internal static class MarshalPolyfills
    {
        public static int GetLastPInvokeError() => Marshal.GetLastWin32Error();
        public static int GetLastPInvokeErrorNative() => Marshal.GetLastWin32Error();
    }
}

namespace System.Security.Cryptography.X509Certificates
{
    /// <summary>X509Certificate2Collection.ImportFromPem polyfill (net5+).</summary>
    internal static class X509CollectionPolyfills
    {
        /// <summary>Reads PEM-encoded certificates (possibly multiple) into the collection.</summary>
        public static void ImportFromPem(this X509Certificate2Collection collection, string pem)
        {
            var lines = pem.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var b64 = new StringBuilder();
            bool inCert = false;
            foreach (var line in lines)
            {
                if (line.StartsWith("-----BEGIN CERTIFICATE-----")) { inCert = true; b64.Clear(); }
                else if (line.StartsWith("-----END CERTIFICATE-----"))
                {
                    inCert = false;
                    if (b64.Length > 0)
                    {
                        try
                        {
                            var bytes = Convert.FromBase64String(b64.ToString());
                            collection.Add(new X509Certificate2(bytes));
                        }
                        catch { /* skip malformed */ }
                        b64.Clear();
                    }
                }
                else if (inCert && !line.StartsWith("-----"))
                {
                    b64.Append(line.Trim());
                }
            }
        }
    }
}

namespace System
{
    /// <summary>Convert.TryFromBase64String polyfill (net5+).</summary>
    internal static class ConvertPolyfills
    {
        public static bool TryFromBase64String(string s, Span<byte> bytes, out int bytesWritten)
        {
            try
            {
                var arr = Convert.FromBase64String(s);
                if (arr.Length > bytes.Length)
                {
                    bytesWritten = 0;
                    return false;
                }
                arr.CopyTo(bytes);
                bytesWritten = arr.Length;
                return true;
            }
            catch
            {
                bytesWritten = 0;
                return false;
            }
        }
    }
}

namespace System.Net.Http
{
    /// <summary>HttpClient.PatchAsync polyfill (net5+).</summary>
    internal static class HttpClientPolyfills
    {
        public static Task<HttpResponseMessage> PatchAsync(this HttpClient client, string requestUri, HttpContent content)
        {
            return client.SendAsync(new HttpRequestMessage(new HttpMethod("PATCH"), requestUri) { Content = content });
        }
        public static Task<HttpResponseMessage> PatchAsync(this HttpClient client, Uri requestUri, HttpContent content)
        {
            return client.SendAsync(new HttpRequestMessage(new HttpMethod("PATCH"), requestUri) { Content = content });
        }
        public static Task<HttpResponseMessage> PatchAsync(this HttpClient client, string requestUri, HttpContent content, CancellationToken cancellationToken)
        {
            return client.SendAsync(new HttpRequestMessage(new HttpMethod("PATCH"), requestUri) { Content = content }, cancellationToken);
        }
    }
}
