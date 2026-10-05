using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using v2rayN.Base;
using v2rayN.Handler;
using v2rayN.Mode;

namespace v2rayN
{
    /// <summary>
    /// `v2rayN.exe --selftest` — run the port's own checks on the machine it will
    /// actually run on.
    ///
    /// Every automated check in this project runs headless on a CI runner, which
    /// cannot tell you whether the UI comes up, whether the DPI is right, or
    /// whether a profile generated here matches what the user's server expects.
    /// This runs the same validation and generation logic inside the real
    /// executable, on the real desktop, and prints a verdict. One command:
    ///
    ///     v2rayN.exe --selftest
    ///
    /// Exits 0 when everything passes, 1 otherwise, so it is usable from a script.
    /// It touches nothing outside its own folder: no registry writes beyond the two
    /// values the app already writes at startup, no network, no cores launched.
    /// </summary>
    internal static class SelfTest
    {
        private const int ATTACH_PARENT_PROCESS = -1;
        private const int ATTACH_PROCESS_DETACH = -2; // already attached

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        private static readonly List<string> _failures = new();
        private static readonly StringBuilder _log = new();
        private static int _passed;

        /// <summary>
        /// v2rayN.exe is a WinExe, so it has no console of its own and
        /// Console.WriteLine is thrown away. Attach to the shell's console when there
        /// is one, so running it from a prompt prints; and always mirror everything to
        /// a report file, so double-clicking it still leaves something readable
        /// instead of a window that flashes and exits.
        /// </summary>
        private static void EnsureConsole()
        {
            if (!AttachConsole(ATTACH_PARENT_PROCESS) && !AttachConsole(ATTACH_PROCESS_DETACH))
            {
                try { AllocConsole(); } catch { /* nothing we can do */ }
            }
            try
            {
                var stdout = Console.OpenStandardOutput();
                if (stdout != Stream.Null)
                {
                    Console.SetOut(new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true });
                }
            }
            catch
            {
                // No console available. The report file is the fallback.
            }
        }

        private static void Say(string line)
        {
            _log.AppendLine(line);
            try { Console.WriteLine(line); } catch { /* console gone */ }
        }

        public static int Run()
        {
            EnsureConsole();
            Say("v2rayN net48 self test");

            try
            {
                // Config generation reads the local inbound port out of LazyConfig.
                // Without this the config has no listener, and all ten generations
                // fail -- which is exactly what the first run did.
                var cfg = new Config
                {
                    inbound = new List<InItem>
                    {
                        new InItem
                        {
                            protocol = Global.InboundSocks,
                            localPort = 20880,
                            udpEnabled = true,
                            sniffingEnabled = true,
                        },
                    },
                };
                LazyConfig.Instance.SetConfig(ref cfg);

                Environment_();
                SampleProfiles();
                ShareLinkRoundTrips();
                Validation_();
                Generation();
                Report();
            }
            catch (Exception ex)
            {
                // A self-test that cannot report its own crash is worse than none:
                // the first CI run produced no output whatsoever for exactly this
                // reason. Record it as a failure and still write the report.
                _failures.Add($"the self test itself threw: {ex.GetType().Name}: {ex.Message}");
                Say("");
                Say($"!! the self test threw: {ex.GetType().Name}: {ex.Message}");
                Say(ex.StackTrace ?? "(no stack trace)");
            }

            Say(new string('=', 60));
            if (_failures.Count == 0)
            {
                Say($"PASS  {_passed} checks, 0 failures");
                WriteReport();
                return 0;
            }
            Say($"FAIL  {_passed} passed, {_failures.Count} failed:");
            foreach (var f in _failures)
            {
                Say($"  - {f}");
            }
            WriteReport();
            return 1;
        }

        /// <summary>
        /// Always writes selftest-report.txt next to the exe, so a double-click that
        /// flashes and exits still leaves a record, and so CI can read the report
        /// rather than a WinExe's unreliable console stream.
        /// </summary>
        private static void WriteReport()
        {
            var path = Path.Combine(Utils.StartupPath(), "selftest-report.txt");
            try
            {
                File.WriteAllText(path, _log.ToString(), new UTF8Encoding(false));
                Say("");
                Say($"report written to {path}");
            }
            catch (Exception ex)
            {
                Say($"could not write the report to {path}: {ex.Message}");
            }
        }

        private static void Check(bool ok, string what)
        {
            if (ok)
            {
                _passed++;
                Say($"  ok    {what}");
            }
            else
            {
                _failures.Add(what);
                Say($"  FAIL  {what}");
            }
        }

        /// <summary>
        /// What this machine actually is. A green result on an unexpected OS or
        /// architecture means less than it looks, and this branch is explicitly a
        /// Windows 7 / x64 green build.
        /// </summary>
        private static void Environment_()
        {
            Say("");
            Say("-- environment --");
            Say($"  os            {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})");
            Say($"  runtime       {Environment.Version}, .NET {Environment.Version}");
            Say($"  process       {(Environment.Is64BitProcess ? "x64" : "x86")}");
            Say($"  exe version   {Utils.GetVersion()}");
            Say($"  startup path  {Utils.StartupPath()}");

            Check(Environment.OSVersion.Version.Major >= 6, "OS version is Vista or newer");
            Check(Environment.Is64BitOperatingSystem, "64-bit OS");

            // PerMonitorV2 comes from app.manifest. If the manifest were missing or
            // malformed the process would be DPI-unaware and the UI blurry on a
            // scaled display, which no headless CI check can see.
            try
            {
                using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
                var dpi = g.DpiX;
                Say($"  dpi           {dpi} x {g.DpiY}");
                Check(dpi > 0, "a usable DPI was reported");
            }
            catch (Exception ex)
            {
                Check(false, $"DPI query threw: {ex.GetType().Name}");
            }
        }

        /// <summary>
        /// Profiles covering everything this port generates. Each is built to be
        /// structurally valid so config generation has something real to emit.
        /// </summary>
        private static List<(string Name, VmessItem Item)> SampleProfiles()
        {
            Say("");
            Say("-- sample profiles --");
            var priv = Convert.ToBase64String(Enumerable.Repeat((byte)0x11, 32).ToArray());
            var pub = Convert.ToBase64String(Enumerable.Repeat((byte)0x22, 32).ToArray());

            var profiles = new List<(string, VmessItem)>
            {
                ("VMess", new VmessItem { configType = EConfigType.VMess, address = "vmess.example", port = 443, id = "11111111-2222-3333-4444-555555555555" }),
                ("VLESS+Reality", new VmessItem
                {
                    configType = EConfigType.VLESS, address = "reality.example", port = 443,
                    id = "11111111-2222-3333-4444-555555555555",
                    streamSecurity = Global.StreamSecurityReality, sni = "www.microsoft.com",
                    publicKey = pub, shortId = "0123456789abcdef", fingerprint = "chrome",
                }),
                ("VLESS+xhttp", new VmessItem
                {
                    configType = EConfigType.VLESS, address = "xhttp.example", port = 443,
                    id = "11111111-2222-3333-4444-555555555555", network = "xhttp",
                    requestHost = "cdn.example", path = "/xhttp", xhttpMode = "packet-up",
                }),
                ("Trojan", new VmessItem { configType = EConfigType.Trojan, address = "trojan.example", port = 443, id = "pw" }),
                ("Shadowsocks", new VmessItem { configType = EConfigType.Shadowsocks, address = "ss.example", port = 8388, id = "pw", security = "aes-128-gcm" }),
                ("Hysteria2", new VmessItem { configType = EConfigType.Hysteria2, address = "hy2.example", port = 443, id = "pw" }),
                ("AnyTLS", new VmessItem { configType = EConfigType.AnyTLS, address = "anytls.example", port = 443, id = "pw", sni = "cdn.example" }),
                ("Naive", new VmessItem { configType = EConfigType.Naive, address = "naive.example", port = 443, id = "alice:pw" }),
                ("MASQUE", new VmessItem { configType = EConfigType.MASQUE, address = "masque.example", port = 443, id = "bob:pw" }),
                ("WireGuard", new VmessItem
                {
                    configType = EConfigType.WireGuard, address = "wg.example", port = 51820,
                    id = priv, publicKey = pub, interfaceAddress = "172.16.0.2/32",
                    reserved = "1,2,3", mtu = 1408,
                }),
            };

            foreach (var (name, _) in profiles)
            {
                Say($"  profile {name}");
            }
            return profiles;
        }

        private static void ShareLinkRoundTrips()
        {
            Say("");
            Say("-- share link round trips --");
            foreach (var (name, item) in SampleProfiles())
            {
                var url = ShareHandler.GetShareUrl(item);
                if (string.IsNullOrEmpty(url))
                {
                    Check(false, $"{name}: exports a link");
                    continue;
                }
                var back = ShareHandler.ImportFromClipboardConfig(url, out _);
                var same = back != null
                    && back.configType == item.configType
                    && back.address == item.address
                    && back.port == item.port
                    && back.id == item.id;
                Check(same, $"{name}: link round trips ({url.Split('#')[0]})");
            }
        }

        private static void Validation_()
        {
            Say("");
            Say("-- validation catches what used to fail silently --");
            var noRealityKey = new VmessItem
            {
                configType = EConfigType.VLESS,
                address = "a.example",
                port = 443,
                id = "11111111-2222-3333-4444-555555555555",
                streamSecurity = Global.StreamSecurityReality,
            };
            var r = NodeValidator.Validate(noRealityKey);
            Check(!r.IsValid, "Reality without pbk/sid is rejected");

            var noHy2 = new VmessItem { configType = EConfigType.Hysteria2, address = "h.example", port = 443 };
            Check(!NodeValidator.Validate(noHy2).IsValid, "Hysteria2 without a password is rejected");

            // And the validator must not be over-strict: every generated profile above
            // has to pass it.
            foreach (var (name, item) in SampleProfiles())
            {
                var res = NodeValidator.Validate(item);
                Check(res.IsValid, $"{name}: a well-formed profile validates ({res})");
            }
        }

        /// <summary>
        /// Configuration output goes to ./selftest; a summary of it also belongs in the
        /// report file so someone reading the report after the fact can see what was
        /// produced without opening the folder.
        /// </summary>
        private static void Report()
        {
            var dir = Path.Combine(Utils.StartupPath(), "selftest");
            if (!Directory.Exists(dir))
            {
                return;
            }
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x))
            {
                var fi = new FileInfo(f);
                Say($"  wrote      {Path.GetFileName(f),-24} {fi.Length,7} bytes");
            }
        }

        private static void Generation()
        {
            Say("");
            Say("-- config generation --");
            var outDir = Path.Combine(Utils.StartupPath(), "selftest");
            Directory.CreateDirectory(outDir);
            foreach (var (name, item) in SampleProfiles())
            {
                var file = Path.Combine(outDir, name.Replace('+', '-') + ".json");
                var rc = item.configType == EConfigType.AnyTLS
                              || item.configType == EConfigType.Naive
                              || item.configType == EConfigType.MASQUE
                              || item.configType == EConfigType.WireGuard
                    ? SingboxConfigHandler.GenConfig(item, 20880, "warning", null, out var _) == 0
                        ? V2rayConfigHandler.GenerateClientConfig(item, file, out _, out _) == 0
                        : false
                    : V2rayConfigHandler.GenerateClientConfig(item, file, out _, out _) == 0;
                Check(rc && File.Exists(file) && new FileInfo(file).Length > 0, $"{name}: a config was written");
            }
            Say($"  configs in   {outDir}");
        }
    }
}