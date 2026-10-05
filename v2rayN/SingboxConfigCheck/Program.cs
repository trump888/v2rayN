using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using v2rayN;
using v2rayN.Base;
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
            Console.WriteLine("== masque ==");
            Masque(outDir);

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
            Console.WriteLine("== sing-box config import round-trip ==");
            SingboxImportRoundTrip(outDir);

            Console.WriteLine();
            Console.WriteLine("== core policy and arguments ==");
            CorePolicy();

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
        /// MASQUE. sing-box models it as an <c>endpoints</c> entry typed
        /// "masque-client", not an outbound, so the generated config has to be shaped
        /// differently from AnyTLS and Naive: a direct outbound, a route rule sending
        /// QUIC at the endpoint, and the endpoint itself.
        ///
        /// Structural assertions only. MASQUE is in sing-box *master*
        /// (e22cd5406, 2026-09-21) and still 45 commits ahead of v1.14.2, so no
        /// released binary accepts it. `sing-box check` cannot be run against the
        /// release the app ships; see the build-from-master workflow step.
        /// </summary>
        private static void Masque(string outDir)
        {
            var item = ShareHandler.ImportFromClipboardConfig(
                "masque://alice:s3cr%40t@example.com:443?path=%2Fcustom%2Fpath&sni=cdn.example.org&insecure=1#MQ",
                out var msg);
            Check(item != null, $"masque:// imports (msg: {msg})");
            if (item == null) { return; }

            Check(item.configType == EConfigType.MASQUE, "imported type is MASQUE");
            Check(item.address == "example.com" && item.port == 443, "address and port");
            Check(item.id == "alice:s3cr@t", $"credential decodes (got '{item.id}')");
            Check(item.path == "/custom/path", $"path decodes (got '{item.path}')");
            Check(item.sni == "cdn.example.org", "sni");
            Check(item.allowInsecure == "1", "insecure");

            var again = ShareHandler.ImportFromClipboardConfig(ShareHandler.GetShareUrl(item), out _);
            Check(again != null && again.id == item.id && again.path == item.path
                  && again.address == item.address && again.port == item.port
                  && again.sni == item.sni && again.remarks == item.remarks,
                  "masque survives an export/import cycle unchanged");

            var cfg = new Config
            {
                inbound = new List<InItem>
                {
                    new InItem { protocol = Global.InboundSocks, localPort = 10808, udpEnabled = true, sniffingEnabled = true },
                },
            };
            LazyConfig.Instance.SetConfig(ref cfg);

            var file = Path.Combine(outDir, "masque.json");
            var rc = V2rayConfigHandler.GenerateClientConfig(item, file, out var genMsg, out _);
            Check(rc == 0, $"MASQUE generates (msg: {genMsg})");
            if (rc != 0 || !File.Exists(file)) { return; }

            var json = File.ReadAllText(file);
            Check(json.Contains("\"endpoints\""), "config has a top-level endpoints array");
            Check(json.Contains("\"type\": \"masque-client\""), "endpoint is typed masque-client");
            Check(json.Contains("\"tag\": \"proxy-endpoint\""), "endpoint is tagged proxy-endpoint");
            Check(json.Contains("\"username\": \"alice\""), "endpoint username is split out of the userinfo");
            Check(json.Contains("\"password\": \"s3cr@t\""), "endpoint password is the part after the colon");
            Check(json.Contains("\"path\": \"/custom/path\""), "endpoint carries the path");
            Check(json.Contains("\"network\": \"quic\""), "a route rule sends QUIC at the endpoint");
            Check(json.Contains("\"listen_port\": 10808"), "mixed inbound uses the configured local port");
            Check(!json.Contains("\"type\": \"masque\""), "not emitted as an outbound (sing-box has no masque outbound)");

            Check(SingboxConfigHandler.IsEndpointType(EConfigType.MASQUE), "MASQUE is classified as an endpoint type");
            Check(!SingboxConfigHandler.IsEndpointType(EConfigType.AnyTLS), "AnyTLS is not an endpoint type");
            Check((int)EConfigType.MASQUE == 14, "MASQUE is 14");
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

                var expectedType = link.StartsWith("masque") ? EConfigType.MASQUE
                    : link.StartsWith("naive") ? EConfigType.Naive : EConfigType.AnyTLS;
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
                ("masque://u:pw@h.example.com:443#Q1", EConfigType.MASQUE),
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
        /// Generate a config with this build's own generator, then parse it back with
        /// the sing-box importer and assert the profile survives. Round-tripping
        /// through the real generator is the point: it proves the two halves agree on
        /// field names, which is what a hand-written fixture would not.
        /// </summary>
        private static void SingboxImportRoundTrip(string outDir)
        {
            var cfg = new Config
            {
                inbound = new List<InItem>
                {
                    new InItem { protocol = Global.InboundSocks, localPort = 10808, udpEnabled = true, sniffingEnabled = true },
                },
            };
            LazyConfig.Instance.SetConfig(ref cfg);

            foreach (var (name, item, wantUser, wantPass) in new[]
            {
                ("anytls", new VmessItem { configType = EConfigType.AnyTLS, address = "a.example", port = 443,
                    id = "pw", sni = "s.example", allowInsecure = "true",
                    alpn = new List<string> { "h2" }, fingerprint = "chrome" }, null, "pw"),
                ("naive", new VmessItem { configType = EConfigType.Naive, address = "n.example", port = 8443,
                    id = "alice:s3cr@t", sni = "n.sni" }, "alice", "s3cr@t"),
                ("masque", new VmessItem { configType = EConfigType.MASQUE, address = "q.example", port = 443,
                    id = "bob:pw", sni = "q.sni", path = "/p" }, "bob", "pw"),
            })
            {
                var file = Path.Combine(outDir, "rt-" + name + ".json");
                var rc = V2rayConfigHandler.GenerateClientConfig(item, file, out var msg, out _);
                Check(rc == 0 && File.Exists(file), $"{name}: generated for round-trip (msg: {msg})");
                if (rc != 0 || !File.Exists(file)) { continue; }

                var text = File.ReadAllText(file);
                Check(SingboxConfigImporter.LooksLikeSingbox(text), $"{name}: its own output is recognised as sing-box");

                var parsed = SingboxConfigImporter.Resolve(text);
                Check(parsed.Count == 1, $"{name}: round-trip yields one profile (got {parsed.Count})");
                if (parsed.Count != 1) { continue; }
                var back = parsed[0];

                Check(back.configType == item.configType, $"{name}: type survives ({back.configType})");
                Check(back.address == item.address, $"{name}: address survives ('{back.address}')");
                Check(back.port == item.port, $"{name}: port survives ({back.port})");
                Check(back.sni == item.sni, $"{name}: sni survives ('{back.sni}')");
                Check(back.allowInsecure == item.allowInsecure, $"{name}: insecure survives ('{back.allowInsecure}')");
                Check((back.alpn ?? new List<string>()).SequenceEqual(item.alpn ?? new List<string>()),
                      $"{name}: alpn survives ({string.Join(",", back.alpn ?? new List<string>())})");
                Check(back.fingerprint == item.fingerprint, $"{name}: fingerprint survives ('{back.fingerprint}')");
                Check(back.id == item.id, $"{name}: credential survives ('{back.id}')");
                if (wantUser != null)
                {
                    Check(back.id == wantUser + ":" + wantPass, $"{name}: userinfo stays 'user:pass'");
                }
                if (item.configType == EConfigType.MASQUE)
                {
                    Check(back.path == item.path, $"masque: path survives ('{back.path}')");
                }
            }

            // A pasted multi-outbound config must yield several profiles, and the
            // helper outbounds must not become servers.
            var multi = @"{
              ""outbounds"": [
                { ""type"": ""direct"", ""tag"": ""direct"" },
                { ""type"": ""block"", ""tag"": ""block"" },
                { ""type"": ""anytls"", ""tag"": ""one"", ""server"": ""x.example"", ""server_port"": 443,
                  ""password"": ""p1"", ""tls"": { ""enabled"": true, ""server_name"": ""x.sni"" } },
                { ""type"": ""trojan"", ""tag"": ""two"", ""server"": ""y.example"", ""server_port"": 8443,
                  ""password"": ""p2"", ""tls"": { ""enabled"": true, ""server_name"": ""y.sni"" } }
              ] }";
            Check(SingboxConfigImporter.LooksLikeSingbox(multi), "a multi-outbound config is recognised");
            var many = SingboxConfigImporter.Resolve(multi);
            Check(many.Count == 2, $"only the two real proxies are imported (got {many.Count}: {string.Join(",", many.Select(x => x.configType))})");

            // And a v2ray config must NOT be claimed by the sing-box importer --
            // both use "outbounds", and confusing them would store a sing-box config
            // as a v2ray custom config.
            var v2rayish = @"{ ""inbounds"": [ { ""port"": 10808, ""protocol"": ""socks"" } ],
                              ""outbounds"": [ { ""protocol"": ""freedom"" } ] }";
            Check(!SingboxConfigImporter.LooksLikeSingbox(v2rayish), "a v2ray config is not claimed by the sing-box importer");
            Check(SingboxConfigImporter.Resolve(v2rayish).Count == 0, "and resolves to nothing");

            // End to end through the clipboard path the UI actually uses.
            var cfg2 = new Config { vmess = new List<VmessItem>(), subItem = new List<SubItem>() };
            var pasted = File.ReadAllText(Path.Combine(outDir, "rt-anytls.json"));
            var n = ConfigHandler.AddBatchServers(ref cfg2, pasted, "", "g5");
            Check(n == 1, $"pasting a generated sing-box config imports one server (got {n})");
            Check(cfg2.vmess.FirstOrDefault()?.configType == EConfigType.AnyTLS,
                  "and it is an AnyTLS profile, not a v2ray custom config");
        }

        /// <summary>
        /// The core table as a whole: every offered core resolves, none of them is
        /// superseded, the enum values that saved profiles depend on have not moved,
        /// and the {0} argument substitution works.
        /// </summary>
        private static void CorePolicy()
        {
            var lazy = LazyConfig.Instance;

            foreach (var name in Global.coreTypes)
            {
                var ok = Enum.TryParse<ECoreType>(name, out var ct) && lazy.GetCoreInfo(ct) != null;
                Check(ok, $"offered core '{name}' has a CoreInfo registration");
            }

            // Policy: a gone project with a successor must not be offered. These are
            // the ones with successors; mieru is deliberately absent because it has
            // none and is kept.
            foreach (var gone in new[] { "clash", "clash_meta", "SagerNet" })
            {
                Check(!Global.coreTypes.Contains(gone),
                      $"'{gone}' has a successor (mihomo / v2fly) and is not offered");
            }
            Check(Global.coreTypes.Contains("mieru"),
                  "mieru has no successor, so it stays offered");
            Check(lazy.GetCoreInfo(ECoreType.mieru) != null, "mieru still has a CoreInfo registration");

            // The four cores ported from 7.x, with the exact asset names verified
            // against each repository's latest release.
            var expected = new Dictionary<ECoreType, (string Exe, string Asset)>
            {
                [ECoreType.juicity]     = ("juicity-client", "juicity-windows-x86_64.zip"),
                [ECoreType.brook]       = ("brook_windows_amd64", "brook_windows_amd64.exe"),
                [ECoreType.overtls]     = ("overtls-bin", "overtls-x86_64-win7-windows-msvc.zip"),
                [ECoreType.shadowquic]  = ("shadowquic", "shadowquic-x86_64-windows.exe"),
            };
            foreach (var kvp in expected)
            {
                var ct = kvp.Key;
                var exe = kvp.Value.Exe;
                var asset = kvp.Value.Asset;

                var info = lazy.GetCoreInfo(ct);
                Check(info != null, $"{ct} is registered");
                if (info == null) { continue; }
                Check(info.coreExes != null && info.coreExes.Contains(exe), $"{ct} exe name is '{exe}'");
                Check(info.coreDownloadUrl64 != null && info.coreDownloadUrl64.Contains(asset),
                      $"{ct} downloads '{asset}'");
                Check(!string.IsNullOrEmpty(info.coreReleaseApiUrl), $"{ct} has a release API url");
            }
            // overtls: the -win7- variant specifically, since this branch targets Win7.
            var otls = lazy.GetCoreInfo(ECoreType.overtls);
            Check(otls != null && otls.coreDownloadUrl64.Contains("-win7-"),
                  "overtls uses the Win7 build, not -pc-");

            // Enum values are persisted in saved profiles and must not move.
            foreach (var (ct, v) in new[]
            {
                (ECoreType.juicity, 25), (ECoreType.brook, 27),
                (ECoreType.overtls, 28), (ECoreType.shadowquic, 29),
                (ECoreType.sing_box, 24), (ECoreType.hysteria2, 26),
                (ECoreType.naiveproxy, 22), (ECoreType.tuic, 23), (ECoreType.hysteria, 21),
                (ECoreType.clash_meta, 12), (ECoreType.Xray, 2), (ECoreType.v2fly, 1),
                (ECoreType.SagerNet, 3), (ECoreType.v2fly_v5, 4), (ECoreType.v2rayN, 99),
            })
            {
                Check((int)ct == v, $"{ct} is still {v}");
            }

            // The {0} substitution. Without it, juicity / overtls / shadowquic /
            // brook cannot be started at all.
            var cfgPath = Utils.GetPath("config.json");
            Check(v2rayN.Handler.V2rayHandler.ResolveArguments("run -c {0}") == "run -c " + cfgPath,
                  "juicity-style argument expands {0}");
            Check(v2rayN.Handler.V2rayHandler.ResolveArguments("{0}") == cfgPath,
                  "brook-style bare {0} expands to just the config path");
            Check(v2rayN.Handler.V2rayHandler.ResolveArguments("-f config.json") == "-f config.json",
                  "an argument with no {0} is left alone (clash_meta)");
            Check(v2rayN.Handler.V2rayHandler.ResolveArguments("") == "", "empty argument stays empty");
            Check(v2rayN.Handler.V2rayHandler.ResolveArguments(null) == "", "null argument becomes empty");
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
            Check((int)EConfigType.MASQUE == 14, "MASQUE is 14");
            Check((int)EConfigType.Mieru == 11, "Mieru is 11 (5.39 predates 7.x reusing that value for Anytls)");
        }
    }
}