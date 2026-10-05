using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            NaiveRoundTrip(outDir);

            Console.WriteLine();
            Console.WriteLine("== full UI clipboard path ==");
            ClipboardPath(outDir);

            Console.WriteLine();
            Console.WriteLine("== routing ==");
            Routing();

            Console.WriteLine();
            Console.WriteLine("== core table ==");
            CoreTable();

            Console.WriteLine();
            Console.WriteLine("== add-server form ==");
            Form();

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

            // Pass a fileName, then read the file. GenerateClientConfig puts the
            // config in the out parameter only when fileName is empty -- that is
            // pre-existing behaviour, and V2rayHandler ignores the out param
            // because it passes a fileName.
            var file = Path.Combine(outDir, "roundtrip.json");
            var rc = V2rayConfigHandler.GenerateClientConfig(item, file, out var genMsg, out var content);
            Check(rc == 0, $"GenerateClientConfig succeeds for AnyTLS (msg: {genMsg})");
            Check(File.Exists(file), "config was written to disk");

            var written = File.Exists(file) ? File.ReadAllText(file) : string.Empty;
            Check(written.Contains("\"type\": \"anytls\""), "generated config is sing-box AnyTLS, not v2ray JSON");
            Check(written.Contains("\"route\""), "generated config is sing-box shaped (has route)");
            Check(!written.Contains("\"log\": {\n    \"access\"") && !written.Contains("\"inbounds\": [\n    {\n      \"tag\": \"socks\""),
                  "generated config is not v2ray-shaped");
            Check(written.Contains("\"listen_port\": 10808"),
                  $"mixed inbound uses the configured local port 10808, not a constant (head: {written.Substring(0, Math.Min(160, written.Length))})");

            // And with no fileName the content must come back through the out param.
            var rc3 = V2rayConfigHandler.GenerateClientConfig(item, null, out _, out var inlineContent);
            Check(rc3 == 0 && inlineContent.Contains("\"type\": \"anytls\""),
                  "with no fileName the config comes back through the out parameter");

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
        /// The path the UI actually takes. MainForm reads the clipboard and calls
        /// ConfigHandler.AddBatchServers, which splits on newlines, routes
        /// subscription-looking data, calls ShareHandler, and falls back through
        /// base64 / SIP008 / custom. Testing ShareHandler directly skips all of
        /// that -- and a link that imports fine in isolation can still fail here.
        /// </summary>
        private static void ClipboardPath(string outDir)
        {
            foreach (var link in new[]
            {
                "anytls://pw@example.com:443#UI1",
                "anytls://u:pw@example.com:443?sni=s.example&insecure=1#UI2",
                "naive+https://pw@example.com:443#UI3",
            })
            {
                var config = new Config { vmess = new List<VmessItem>(), subItem = new List<SubItem>() };
                var n = ConfigHandler.AddBatchServers(ref config, link, "", "group1");
                Check(n == 1, $"AddBatchServers imported {link.Split('#')[0]} (returned {n})");

                var expectedType = link.StartsWith("naive") ? EConfigType.Naive : EConfigType.AnyTLS;
                var added = config.vmess.FirstOrDefault();
                Check(added != null, "  server landed in config.vmess");
                Check(added != null && added.configType == expectedType, "  with the right config type");
                Check(added != null && added.groupId == "group1", "  and the right group");

                // And it must generate a usable config straight after import.
                if (added != null)
                {
                    var f = Path.Combine(outDir, "ui-" + added.configType + ".json");
                    var rc = V2rayConfigHandler.GenerateClientConfig(added, f, out var m, out _);
                    Check(rc == 0, $"  and generates a config immediately (msg: {m})");
                }
            }

            // The same defect hit Hysteria2, Mieru and TUIC before AnyTLS and Naive:
            // AddServer stamps configType = VMess and rejects an empty `security`, so
            // they could not be stored from a link either. Assert the type survives
            // for every protocol that goes through AddTypedServer.
            foreach (var (link, expected) in new[]
            {
                ("hysteria2://pw@h.example.com:443#H1", EConfigType.Hysteria2),
                ("mieru://h.example.com:443?password=pw#M1", EConfigType.Mieru),
                ("tuic://h.example.com:443?uuid=u&password=pw#T1", EConfigType.TUIC),
                ("anytls://h.example.com:443#A1", EConfigType.AnyTLS),
                ("naive+https://h.example.com:443#N1", EConfigType.Naive),
            })
            {
                var c = new Config { vmess = new List<VmessItem>(), subItem = new List<SubItem>() };
                var n = ConfigHandler.AddBatchServers(ref c, link, "", "g4");
                var got = c.vmess.FirstOrDefault()?.configType;
                Check(n == 1 && got == expected,
                      $"{expected} link imports and keeps its type (returned {n}, got {got})");
            }

            // A whole subscription payload: many links, newline separated. This is
            // what a real subscription paste looks like.
            var bulk = string.Join(Environment.NewLine, new[]
            {
                "anytls://a1@h1.example.com:443#A",
                "anytls://a2@h2.example.com:443#B",
                "naive+https://a3@h3.example.com:443#C",
            });
            var bulkCfg = new Config { vmess = new List<VmessItem>(), subItem = new List<SubItem>() };
            var bulkN = ConfigHandler.AddBatchServers(ref bulkCfg, bulk, "", "g2");
            Check(bulkN == 3, $"a 3-link subscription import returns 3 (got {bulkN})");
            Check(bulkCfg.vmess.Count == 3, "and all three are stored");
        }

        /// <summary>
        /// Naive, whose credential is the whole userinfo: "user:pass" or just
        /// "pass". The generator splits it rather than the model gaining a username
        /// field, matching 7.x's NaiveFmt.
        /// </summary>
        private static void NaiveRoundTrip(string outDir)
        {
            Console.WriteLine();
            Console.WriteLine("== naive ==");

            foreach (var (label, link, expectedUser, expectedPass) in new[]
            {
                ("user+pass", "naive+https://alice:s3cr%40t@example.com:443?sni=cdn.example.org&insecure=1#N1", "alice", "s3cr@t"),
                ("pass-only", "naive+https://onlypass@example.com:443#N2", null, "onlypass"),
                ("quic form", "naive+quic://bob:pw@example.com:443#N3", "bob", "pw"),
            })
            {
                var item = ShareHandler.ImportFromClipboardConfig(link, out _);
                Check(item != null, $"naive ({label}) imports");
                if (item == null) { continue; }
                Check(item.configType == EConfigType.Naive, $"naive ({label}) type is Naive");
                Check(item.address == "example.com" && item.port == 443, $"naive ({label}) address/port");

                var file = Path.Combine(outDir, "naive-" + label.Replace('+', '-') + ".json");
                var rc = V2rayConfigHandler.GenerateClientConfig(item, file, out var msg, out _);
                Check(rc == 0, $"naive ({label}) generates (msg: {msg})");
                if (rc != 0 || !File.Exists(file)) { continue; }

                var json = File.ReadAllText(file);
                Check(json.Contains("\"type\": \"naive\""), $"naive ({label}) emits a naive outbound");
                if (expectedUser != null)
                {
                    Check(json.Contains($"\"username\": \"{expectedUser}\""), $"naive ({label}) splits username '{expectedUser}'");
                }
                else
                {
                    Check(!json.Contains("\"username\""), $"naive ({label}) omits username when none was given");
                }
                Check(json.Contains($"\"password\": \"{expectedPass.Replace("@", "@")}\""), $"naive ({label}) sets password '{expectedPass}'");
            }
        }

        /// <summary>
        /// The WinForms dialog, exercised for real. A Windows runner can construct
        /// WinForms without an interactive desktop, so this is not eyeballing a
        /// screenshot: it drives the same code paths the user does.
        ///
        /// This is here because it found a real bug. AddServerForm used to call
        /// AddNewProtocolControls() from its constructor, but MainForm assigns
        /// eConfigType only *after* the constructor returns, so eConfigType was 0 and
        /// no protocol branch ever matched. Hysteria2, Mieru and TUIC had been
        /// showing no Up/Down Mbps, Obfs or Cert SHA256 fields, and the null guards
        /// in BindingServer turned that into silence rather than an error.
        /// </summary>
        private static void Form()
        {
            try
            {
                using var f = new v2rayN.Forms.AddServerForm();
                Check(f != null, "AddServerForm constructs headlessly");

                // Same order MainForm.ShowServerForm uses: construct, then set.
                f.eConfigType = EConfigType.AnyTLS;
                Check(f.eConfigType == EConfigType.AnyTLS, "form accepts AnyTLS as its protocol");

                // Now drive Load the way the runtime does. OnLoad is what actually
                // calls AddNewProtocolControls; raising it directly is what a
                // ShowDialog would do.
                var onLoad = typeof(v2rayN.Forms.AddServerForm)
                    .GetMethod("AddServerForm_Load",
                               System.Reflection.BindingFlags.Instance |
                               System.Reflection.BindingFlags.NonPublic);
                Check(onLoad != null, "AddServerForm_Load is reachable for the test");
                if (onLoad != null)
                {
                    onLoad.Invoke(f, new object[] { f, EventArgs.Empty });
                }

                Check(f.Controls.Count > 0, "form has controls after Load");

                // AnyTLS-specific controls, created dynamically by AddNewProtocolControls.
                var sni = FindControl(f, "txtProtocolSni");
                var fp = FindControl(f, "cmbProtocolFingerprint");
                var insecure = FindControl(f, "chkProtocolInsecure");
                Check(sni != null, "protocol SNI field is created");
                Check(fp != null, "protocol fingerprint combo is created");
                Check(insecure != null, "protocol allow-insecure checkbox is created");
                Check(FindControl(f, "txtUpMbps") == null,
                      "these protocols do not get Up/Down Mbps (a bandwidth knob is meaningless for a TLS protocol)");

                // The AnyTLS controls must not reuse a name from another panel.
                // ServerTransportControl already owns cmbFingerprint; a second
                // control with that name in the same form breaks lookup and
                // accessibility, and would make this test pass for the wrong reason.
                var transportFp = FindControl(f, "cmbFingerprint");
                Check(transportFp == null || !ReferenceEquals(transportFp, fp),
                      "AnyTLS fingerprint combo does not collide with ServerTransportControl's");

                // And the regression: the same must now be true for Hysteria2, whose
                // fields were the ones silently missing.
                using var h2 = new v2rayN.Forms.AddServerForm();
                h2.eConfigType = EConfigType.Hysteria2;
                var h2load = typeof(v2rayN.Forms.AddServerForm)
                    .GetMethod("AddServerForm_Load",
                               System.Reflection.BindingFlags.Instance |
                               System.Reflection.BindingFlags.NonPublic);
                h2load?.Invoke(h2, new object[] { h2, EventArgs.Empty });
                Check(FindControl(h2, "txtUpMbps") != null,
                      "Hysteria2 Up/Down Mbps fields now exist (were silently missing)");
                Check(FindControl(h2, "cmbObfs") != null,
                      "Hysteria2 Obfs combo now exists (were silently missing)");
                Check(FindControl(h2, "txtCertSha256") != null,
                      "Hysteria2 Cert SHA256 field now exists (were silently missing)");
            }
            catch (Exception ex)
            {
                Check(false, $"form test threw: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Find a dynamically-added control. AddNewProtocolControls builds these in
        /// code rather than in the designer, so they have no generated field to
        /// reference and have to be located by walking the control tree.
        /// </summary>
        private static System.Windows.Forms.Control FindControl(System.Windows.Forms.Control root, string name)
        {
            foreach (System.Windows.Forms.Control c in root.Controls)
            {
                if (c.Name == name) return c;
                var hit = FindControl(c, name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>
        /// The routing predicate has to agree with the enum in both directions. A
        /// predicate that returned true for everything would compile, pass the
        /// config check, and break every existing v2ray profile at runtime.
        /// </summary>
        private static void Routing()
        {
            Check(SingboxConfigHandler.IsSingboxOnly(EConfigType.AnyTLS), "AnyTLS is routed to the sing-box generator");
            Check(SingboxConfigHandler.IsSingboxOnly(EConfigType.Naive), "Naive is routed to the sing-box generator");
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
            Check((int)EConfigType.Naive == 13, "Naive is 13");
        }
    }
}