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

        public static Task<string> GetStringAsync(this HttpClient client, string requestUri, CancellationToken cancellationToken)
        {
            return client.GetStringAsync(requestUri);
        }
        public static Task<string> GetStringAsync(this HttpClient client, Uri requestUri, CancellationToken cancellationToken)
        {
            return client.GetStringAsync(requestUri);
        }
    }
}

// ---------------------------------------------------------------------------
// Process.Kill(bool) — net48 Process.Kill() takes no args; .NET 5+ added
// Kill(bool entireProcessTree). We provide as extension method.
// ---------------------------------------------------------------------------

namespace System.Diagnostics
{
    internal static class ProcessPolyfills
    {
        public static void Kill(this Process process, bool entireProcessTree)
        {
            // net48: just kill the process itself, ignore tree
            process.Kill();
        }
    }
}

// ---------------------------------------------------------------------------
// Stream.ReadAsync(Memory<byte>, CancellationToken) — .NET Core 2.1+
// Stream.WriteAsync(ReadOnlyMemory<byte>, CancellationToken) — .NET Core 2.1+
// StreamReader.ReadToEndAsync(CancellationToken) — .NET 7+
// ---------------------------------------------------------------------------

namespace System.IO
{
    internal static class StreamPolyfills
    {
        public static async Task<int> ReadAsync(this Stream stream, System.Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var arr = buffer.ToArray();
            int read = await stream.ReadAsync(arr, 0, arr.Length, cancellationToken);
            arr.CopyTo(buffer.Span);
            return read;
        }
        public static async Task WriteAsync(this Stream stream, System.ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await stream.WriteAsync(buffer.ToArray(), 0, buffer.Length, cancellationToken);
        }
        public static async Task<int> ReadAsync(this StreamReader reader, System.Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // StreamReader doesn't have ReadAsync(Memory<byte>); read char-by-char
            var arr = buffer.ToArray();
            int read = 0;
            char[] charBuf = new char[1];
            while (read < arr.Length)
            {
                int n = await reader.ReadAsync(charBuf, 0, 1);
                if (n == 0) break;
                arr[read++] = (byte)charBuf[0];
            }
            arr.CopyTo(buffer.Span);
            return read;
        }
        public static Task<string> ReadToEndAsync(this StreamReader reader, CancellationToken cancellationToken)
        {
            // net48 StreamReader.ReadToEndAsync() doesn't accept CancellationToken;
            // just ignore it
            return reader.ReadToEndAsync();
        }
    }
}

// ---------------------------------------------------------------------------
// TcpClient.ConnectAsync(string host, int port, CancellationToken) — .NET 5+
// ---------------------------------------------------------------------------

namespace System.Net.Sockets
{
    internal static class TcpClientPolyfills
    {
        public static async Task ConnectAsync(this TcpClient client, string host, int port, CancellationToken cancellationToken)
        {
            // net48: ConnectAsync doesn't accept CancellationToken
            await client.ConnectAsync(host, port);
        }
        public static async Task ConnectAsync(this TcpClient client, IPAddress address, int port, CancellationToken cancellationToken)
        {
            await client.ConnectAsync(address, port);
        }
    }
}

// ---------------------------------------------------------------------------
// Architecture.RiscV64 / LoongArch64 — .NET 7+ enum values
// ---------------------------------------------------------------------------

namespace System.Runtime.InteropServices
{
    internal static class ArchitecturePolyfills
    {
        // Architecture enum already exists in net48 but lacks RiscV64/LoongArch64
        // We provide a static helper that returns false for these (they don't
        // exist on net48 anyway)
        public static bool IsRiscV64 => false;
        public static bool IsLoongArch64 => false;
    }
}

// ---------------------------------------------------------------------------
// Socket.ConnectAsync(SocketType, ProtocolType, string, int, CancellationToken)
// — .NET 5+ overload. net48 only has ConnectAsync(string, int).
// ---------------------------------------------------------------------------

namespace System.Net.Sockets
{
    internal static class SocketPolyfills
    {
        public static async Task ConnectAsync(this Socket socket, SocketType socketType, ProtocolType protocolType, string host, int port)
        {
            await Task.Run(() => socket.Connect(host, port));
        }
    }
}

// ---------------------------------------------------------------------------
// string.Replace(string, string, StringComparison) — .NET Core 2.0+
// net48 only has Replace(string, string) (culture-sensitive)
// ---------------------------------------------------------------------------

namespace System
{
    internal static class StringReplacePolyfills
    {
        public static string Replace(this string s, string oldValue, string newValue, StringComparison comparisonType)
        {
            // Simple implementation: use IndexOf with comparison, then build result
            if (string.IsNullOrEmpty(oldValue)) return s;
            var result = new System.Text.StringBuilder();
            int idx = 0;
            while (idx < s.Length)
            {
                int found = s.IndexOf(oldValue, idx, comparisonType);
                if (found < 0)
                {
                    result.Append(s, idx, s.Length - idx);
                    break;
                }
                result.Append(s, idx, found - idx);
                result.Append(newValue);
                idx = found + oldValue.Length;
            }
            return result.ToString();
        }
    }
}
