using System;
using System.Collections.Generic;
using System.IO;
using v2rayN;
using v2rayN.Handler;
using v2rayN.Mode;

namespace SingboxConfigCheck
{
    /// <summary>
    /// Runtime tests for the net48 port, run on the Windows CI runner.
    ///
    /// This exists because compiling proves almost nothing about this change. A
    /// wrong field name in a config generator, a share link that loses the
    /// password, or a routing predicate that sends v2ray JSON to sing-box all
    /// compile cleanly and then fail on a user's machine. A net48 console
    /// executable runs on the runner, so these can be asserted for real rather
    /// than eyeballed.
    ///
    /// The config files it writes are additionally run through `sing-box check` by
    /// the workflow. Assertions here cover the parts only this process can see:
    /// round-tripping, routing, port selection and the core table.
    ///
    /// Not in v2rayN.sln and never shipped.
    /// </summary>
    internal static class Program
    {
        private static int _failed;
        private static int _passed;

        private static void Check(bool condition, string what)
        {
            if (condition)
            {
                _passed++;
                Console.WriteLine($"  pass  {what}");
            }
            else
            {
                _failed++;
                Console.WriteLine($"  FAIL  {what}");
            }
        }

        private static int Main(string[] args)
        {
            var outDir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(outDir);

            Console.WriteLine("== share link round-trip ==");
            RoundTrip(outDir);

            Console.WriteLine();
            Console.WriteLine("== routing ==");
            Routing();

            Console.WriteLine();
            Console.WriteLine("== core table ==");
            CoreTable();

            Console.WriteLine();
            Console.WriteLine($"{_passed} passed, {_failed} failed");
            return _failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// Import a link, re-export it, and import it again -- through the real
        /// public entry points, not the private helpers. Catches a parser that
        /// drops the password, a serialiser that emits a different shape than the
        /// parser accepts, and a mismatch between the two.
        /// </summary>
        private static void RoundTrip(string outDir)
        {
            const string original = "anytls://s3cr%40t%3Aword@example.com:8443?sni=cdn.example.org&alpn=h2%2Chttp%2F1.1&insecure=1&fingerprint=chrome#My%20Node";

            var item = ShareHandler.ImportFromClipboardConfig(original, out var msg);
            Check(item != null, "anytls:// imports");
            if (item == null)
            {
                Console.WriteLine($"        msg: {msg}");
                return;
            }

            Check(item.configType == EConfigType.AnyTLS, "imported type is AnyTLS");
            Check(item.address == "example.com", $"address round-trips (got '{item.address}')");
            Check(item.port == 8443, $"port round-trips (got {item.port})");
            // The whole userinfo is the password, and it arrives percent-encoded.
            Check(item.id == "s3cr@t:word", $"password decodes correctly (got '{item.id}')");
            Check(item.sni == "cdn.example.org", $"sni round-trips (got '{item.sni}')");
            Check(item.allowInsecure == "1", $"insecure round-trips (got '{item.allowInsecure}')");
            Check(item.fingerprint == "chrome", $"fingerprint round-trips (got '{item.fingerprint}')");
            Check(item.alpn != null && item.alpn.Count == 2 && item.alpn[0] == "h2" && item.alpn[1] == "http/1.1",
                  $"alpn round-trips (got '{string.Join(",", item.alpn ?? new List<string>())}')");
            Check(item.remarks == "My Node", $"remark round-trips (got '{item.remarks}')");

            var url = ShareHandler.GetShareUrl(item);
            Check(url != null && url.StartsWith("anytls://"), $"exports as anytls:// (got '{url}')");

            var again = ShareHandler.ImportFromClipboardConfig(url, out _);
            Check(again != null, "re-exported link re-imports");
            if (again == null)
            {
                return;
            }

            Check(again.address == item.address && again.port == item.port && again.id == item.id
                  && again.sni == item.sni && again.allowInsecure == item.allowInsecure
                  && again.fingerprint == item.fingerprint && again.remarks == item.remarks,
                  "link survives a full export/import cycle unchanged");

            // And the import must actually produce a config sing-box accepts.
            // LazyConfig needs a config for the local port, so give it a realistic one.
            var cfg = new Config();
            cfg.inbound = new List<InItem>
            {
                new InItem { protocol = Global.InboundSocks, localPort = 10808, udpEnabled = true, sniffingEnabled = true },
            };
            LazyConfig.Instance.SetConfig(ref cfg);

            var file = Path.Combine(outDir, "roundtrip.json");
            var rc = V2rayConfigHandler.GenerateClientConfig(item, file, out var genMsg, out var content);
            Check(rc == 0, $"GenerateClientConfig succeeds for AnyTLS (msg: {genMsg})");
            Check(content.Contains("\"anytls\""), "generated config is sing-box AnyTLS, not v2ray JSON");
            Check(!content.Contains("\"outbounds\"") || content.Contains("\"route\""),
                  "generated config is sing-box shaped (has route, not v2ray inbounds)");
            Check(File.Exists(file), "config was written to disk");
            Check(content.Contains("10808"), $"mixed inbound uses the configured local port (10808), not a constant");

            // A VMess profile must NOT be diverted to the sing-box generator.
            var vmess = new VmessItem
            {
                configType = EConfigType.VMess,
                address = "example.com",
                port = 443,
                id = "b831381d-6324-4d53-ad4f-8cda48b30811",
            };
            var rc2 = V2rayConfigHandler.GenerateClientConfig(vmess, null, out _, out var v2rayContent);
            Check(rc2 == 0, "GenerateClientConfig still succeeds for VMess");
            Check(v2rayContent.Contains("\"outbounds\"") && !v2rayContent.Contains("\"anytls\""),
                  "VMess still generates v2ray JSON");
        }

        /// <summary>
        /// The routing predicate has to agree with the enum in both directions. A
        /// predicate that returned true for everything would compile, pass the
        /// config check, and break every existing v2ray profile at runtime.
        /// </summary>
        private static void Routing()
        {
            Check(SingboxConfigHandler.IsSingboxOnly(EConfigType.AnyTLS), "AnyTLS is routed to the sing-box generator");
            foreach (var t in new[]
            {
                EConfigType.VMess, EConfigType.VLESS, EConfigType.Trojan,
                EConfigType.Shadowsocks, EConfigType.Socks, EConfigType.Hysteria2,
                EConfigType.TUIC, EConfigType.Mieru, EConfigType.Custom,
            })
            {
                Check(!SingboxConfigHandler.IsSingboxOnly(t), $"{t} still uses the v2ray generator");
            }
        }

        /// <summary>
        /// The core table, which is where the rot actually was: one core offered in
        /// the UI but never registered, and one whose exe names no longer match what
        /// its repository publishes.
        /// </summary>
        private static void CoreTable()
        {
            var lazy = LazyConfig.Instance;
            // InitCoreInfo is private; GetCoreInfo calls it, so the first lookup
            // below is what populates the table.

            foreach (var name in Global.coreTypes)
            {
                var ok = Enum.TryParse<ECoreType>(name, out var coreType)
                    && lazy.GetCoreInfo(coreType) != null;
                Check(ok, $"offered core '{name}' has a CoreInfo registration");
            }

            var mihomo = lazy.GetCoreInfo(ECoreType.mihomo);
            Check(mihomo != null, "mihomo is registered");
            if (mihomo != null)
            {
                Check(mihomo.coreExes != null && mihomo.coreExes.Contains("mihomo-windows-amd64"),
                      "mihomo lists mihomo-windows-amd64.exe, the name the current release publishes");
                Check(mihomo.coreDownloadUrl64 != null && mihomo.coreDownloadUrl64.Contains("mihomo-windows-amd64"),
                      "mihomo download URL uses the current asset name");
            }

            // Enum values are persisted in saved profiles, so they must not move.
            Check((int)EConfigType.Mieru == 11, "Mieru is still 11 (saved profiles depend on it)");
            Check((int)EConfigType.AnyTLS == 12, "AnyTLS is 12");
        }
    }
}