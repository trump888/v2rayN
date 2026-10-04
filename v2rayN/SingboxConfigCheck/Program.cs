using System;
using System.Collections.Generic;
using System.IO;
using v2rayN.Handler;
using v2rayN.Mode;

namespace SingboxConfigCheck
{
    /// <summary>
    /// Writes one sing-box configuration per profile shape to stdout's directory.
    /// The workflow then runs `sing-box check` over each; a non-zero exit here is
    /// a generation failure, and a failure there is an invalid config.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var outDir = args.Length > 0 ? args[0] : ".";
            Directory.CreateDirectory(outDir);

            var cases = new List<(string Name, VmessItem Item)>
            {
                ("minimal",     new VmessItem { address = "example.com", port = 443, id = "secret" }),
                ("sni",         new VmessItem { address = "1.2.3.4", port = 8443, id = "p@ss", sni = "cdn.example.org" }),
                ("sni-multi",   new VmessItem { address = "a.example", port = 443, id = "pw", sni = "b.example, c.example" }),
                ("insecure",    new VmessItem { address = "example.com", port = 443, id = "x", allowInsecure = "true" }),
                ("alpn",        new VmessItem { address = "example.com", port = 443, id = "x", alpn = new List<string> { "h2", "http/1.1" } }),
                ("fingerprint", new VmessItem { address = "example.com", port = 443, id = "x", fingerprint = "chrome" }),
                ("everything",  new VmessItem { address = "a.example", port = 443, id = "pw", sni = "b.example, c.example",
                                                 allowInsecure = "true", alpn = new List<string> { "h2" }, fingerprint = "firefox" }),
            };

            var failures = 0;
            foreach (var (name, item) in cases)
            {
                item.configType = EConfigType.AnyTLS;
                var rc = SingboxConfigHandler.GenConfig(item, 2080, "info", null, out var json);
                if (rc != 0 || json == null)
                {
                    Console.Error.WriteLine($"generation failed: {name} (rc={rc})");
                    failures++;
                    continue;
                }
                File.WriteAllText(Path.Combine(outDir, name + ".json"), json);
                Console.WriteLine($"generated {name}.json");
            }

            Console.WriteLine(failures == 0
                ? $"generated {cases.Count} configurations, {failures} failures"
                : $"{failures} generation failures");
            return failures == 0 ? 0 : 1;
        }
    }
}
