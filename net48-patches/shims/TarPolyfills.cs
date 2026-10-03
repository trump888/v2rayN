// =============================================================================
// TarPolyfills.cs  —  NET48 PORT shim
// =============================================================================
// System.Formats.Tar (TarFile) is .NET 7+. net48 has no managed tar reader, and
// the net48 port targets Windows 7 SP1 through Windows 11. Windows 10 1709+ and
// Windows 11 ship `tar.exe` in System32; older builds may not have it.
//
// Strategy: stage the decompressed tar to a temp file, then shell out to
// tar.exe. Staging to disk (rather than piping the stream into the child's
// stdin) avoids the classic deadlock where the child blocks writing stderr
// while we block writing stdin, and it keeps the failure modes simple.
//
// If tar.exe is missing we throw PlatformNotSupportedException with an
// actionable message rather than failing silently, so the user can install
// tar or update manually instead of wondering why a subscription is empty.
// =============================================================================

#if !NET5_0_OR_GREATER

using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace ServiceLib.Common
{
    internal static class TarPolyfills
    {
        /// <summary>
        /// Replaces TarFile.ExtractToDirectory(Stream, string, bool).
        /// </summary>
        /// <param name="tarStream">A seekable stream of uncompressed tar data.</param>
        /// <param name="destinationDirectory">Directory to extract into; created if missing.</param>
        /// <param name="overwriteFiles">When true, replace existing files.</param>
        public static void ExtractToDirectory(
            Stream tarStream,
            string destinationDirectory,
            bool overwriteFiles)
        {
            if (tarStream is null) throw new ArgumentNullException(nameof(tarStream));

            Directory.CreateDirectory(destinationDirectory);

            // tarStream may be non-seekable (a GZipStream), so copy through a
            // temp file either way to keep the tar.exe invocation simple.
            var tempTar = Path.Combine(
                Path.GetTempPath(),
                $"v2rayN-net48-{Guid.NewGuid():N}.tar");
            try
            {
                using (var output = new FileStream(
                    tempTar, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    tarStream.CopyTo(output);
                }

                RunTar(tempTar, destinationDirectory, overwriteFiles);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempTar))
                    {
                        File.Delete(tempTar);
                    }
                }
                catch (IOException)
                {
                    // A leftover temp file is not worth failing a download over.
                }
            }
        }

        private static void RunTar(string tempTar, string destinationDirectory, bool overwriteFiles)
        {
            var args = new StringBuilder()
                .Append("-xf \"").Append(tempTar).Append('"')
                .Append(" -C \"").Append(destinationDirectory).Append('"')
                // tar.exe has no --overwrite-files switch; -o is the portable
                // equivalent for "replace existing files".
                .Append(overwriteFiles ? " -o" : " --keep-old-files")
                .ToString();

            var startInfo = new ProcessStartInfo("tar.exe", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    throw new InvalidOperationException("Failed to start tar.exe.");
                }

                // Read both pipes before waiting, otherwise a chatty tar can
                // fill a pipe buffer and deadlock.
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    throw new PlatformNotSupportedException(
                        "tar.exe exited with code " + process.ExitCode +
                        ". Output: " + stdout + " Error: " + stderr);
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new PlatformNotSupportedException(
                    "tar.exe was not found. .NET Framework 4.8 has no built-in tar " +
                    "reader; install Windows 10 1709+ (which ships tar.exe) or extract " +
                    "the archive manually.", ex);
            }
        }
    }
}

#endif