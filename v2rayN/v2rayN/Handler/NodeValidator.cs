using System;
using System.Collections.Generic;
using System.Linq;
using v2rayN.Base;
using v2rayN.Mode;

namespace v2rayN.Handler
{
    /// <summary>
    /// Validates a profile before it is used, the way 7.x's NodeValidator does.
    ///
    /// Every silent-failure bug found while bringing this branch up to date was the
    /// same shape: the profile parsed, the app accepted it, the config generated,
    /// and the node simply did not work. Reality without pbk, an xhttp link that
    /// fell back to the default transport, Hysteria2 whose `security` was empty so
    /// it was stored as VMess. None of them raised anything.
    ///
    /// A validator is the cheapest possible guard for that class, because it runs
    /// where the user can still see the message. 7.x keeps it in
    /// Handler/Builder/NodeValidator and asserts per protocol; this covers the
    /// protocols this branch can actually generate.
    /// </summary>
    internal static class NodeValidator
    {
        public class Result
        {
            /// <summary>Human-readable problems, in the order they were found.</summary>
            public List<string> Errors { get; } = new();

            /// <summary>Problems that will still let the node start, but probably not work.</summary>
            public List<string> Warnings { get; } = new();

            public bool IsValid => Errors.Count == 0;

            public override string ToString()
            {
                var parts = new List<string>();
                if (Errors.Count > 0)
                {
                    parts.Add(string.Join("; ", Errors));
                }
                if (Warnings.Count > 0)
                {
                    parts.Add(string.Join("; ", Warnings));
                }
                return parts.Count == 0 ? "ok" : string.Join(" | ", parts);
            }
        }

        public static Result Validate(VmessItem? item)
        {
            var r = new Result();
            if (item == null)
            {
                r.Errors.Add("no profile");
                return r;
            }

            if (string.IsNullOrWhiteSpace(item.address))
            {
                r.Errors.Add("address is empty");
            }
            if (item.port <= 0 || item.port > 65535)
            {
                r.Errors.Add($"port {item.port} is out of range");
            }

            // Per-protocol credential. `id` is the credential for every one of
            // these, which is why Hysteria2 leaving it empty was so damaging.
            switch (item.configType)
            {
                case EConfigType.VMess:
                    if (string.IsNullOrWhiteSpace(item.id))
                    {
                        r.Errors.Add("VMess needs a UUID");
                    }
                    else if (!IsGuidLike(item.id))
                    {
                        r.Errors.Add($"VMess id '{item.id}' is not a UUID");
                    }
                    break;

                case EConfigType.VLESS:
                case EConfigType.Trojan:
                    if (string.IsNullOrWhiteSpace(item.id))
                    {
                        r.Errors.Add($"{item.configType} needs an id");
                    }
                    break;

                case EConfigType.Shadowsocks:
                    if (string.IsNullOrWhiteSpace(item.id))
                    {
                        r.Errors.Add("Shadowsocks needs a password");
                    }
                    if (string.IsNullOrWhiteSpace(item.security))
                    {
                        // 5.39 stores the SS method in `security`; without it xray
                        // rejects the whole config.
                        r.Errors.Add("Shadowsocks needs a method (cipher)");
                    }
                    break;

                case EConfigType.Hysteria2:
                case EConfigType.TUIC:
                    if (string.IsNullOrWhiteSpace(item.id))
                    {
                        r.Errors.Add($"{item.configType} needs a password");
                    }
                    break;

                case EConfigType.AnyTLS:
                case EConfigType.Naive:
                    if (string.IsNullOrWhiteSpace(item.id))
                    {
                        r.Errors.Add($"{item.configType} needs a password");
                    }
                    break;

                case EConfigType.MASQUE:
                    if (string.IsNullOrWhiteSpace(item.id))
                    {
                        r.Errors.Add("MASQUE needs a password");
                    }
                    break;

                case EConfigType.WireGuard:
                    if (string.IsNullOrWhiteSpace(item.id))
                    {
                        r.Errors.Add("WireGuard needs a private key");
                    }
                    if (string.IsNullOrWhiteSpace(item.publicKey))
                    {
                        r.Errors.Add("WireGuard needs the peer public key");
                    }
                    if (string.IsNullOrWhiteSpace(item.interfaceAddress))
                    {
                        // sing-box exits with "missing address" or refuses to build.
                        r.Errors.Add("WireGuard needs an interface address, CIDR");
                    }
                    if (string.IsNullOrWhiteSpace(item.allowedIps))
                    {
                        r.Warnings.Add("WireGuard allowed IPs is empty; full tunnel 0.0.0.0/0,::/0 will be assumed");
                    }
                    break;

                case EConfigType.Mieru:
                    if (string.IsNullOrEmpty(item.id))
                    {
                        r.Errors.Add("Mieru needs a password");
                    }
                    break;
            }

            // Transport. An unknown network falls back to tcp silently in
            // GetNetwork(), so an xhttp profile typed by an older build would just
            // quietly become tcp.
            var network = item.GetNetwork();
            if (!Global.networks.Contains(item.network ?? string.Empty) && !string.IsNullOrWhiteSpace(item.network))
            {
                r.Warnings.Add($"transport '{item.network}' is not recognised; falling back to {network}");
            }

            switch (network)
            {
                case "ws":
                case "httpupgrade":
                    // An empty path is legal (defaults to /), so this is only worth
                    // a warning when a host is set without a path, which is usually a
                    // half-filled form.
                    if (!string.IsNullOrWhiteSpace(item.requestHost) && string.IsNullOrWhiteSpace(item.path))
                    {
                        r.Warnings.Add("a host is set but the path is empty; the server may expect a specific path");
                    }
                    break;

                case "xhttp":
                    if (!string.IsNullOrWhiteSpace(item.xhttpMode)
                        && !Global.XhttpMode.Contains(item.xhttpMode))
                    {
                        r.Errors.Add($"xhttp mode '{item.xhttpMode}' is not one of {string.Join(", ", Global.XhttpMode)}");
                    }
                    if (!string.IsNullOrWhiteSpace(item.xhttpExtra))
                    {
                        try
                        {
                            _ = Newtonsoft.Json.Linq.JToken.Parse(item.xhttpExtra);
                        }
                        catch
                        {
                            // Passed through to Xray verbatim, so bad JSON here is a
                            // config that will not start -- worth catching now.
                            r.Errors.Add("xhttp extra is not valid JSON");
                        }
                    }
                    break;
            }

            // TLS / Reality.
            if (item.streamSecurity == Global.StreamSecurityReality)
            {
                if (string.IsNullOrWhiteSpace(item.publicKey))
                {
                    // The exact silent failure: a vless Reality link without pbk
                    // imported cleanly and generated a config that cannot connect.
                    r.Errors.Add("Reality needs the server public key (pbk)");
                }
                if (string.IsNullOrWhiteSpace(item.shortId))
                {
                    r.Errors.Add("Reality needs a short id (sid)");
                }
                if (string.IsNullOrWhiteSpace(item.sni))
                {
                    r.Warnings.Add("Reality usually needs an SNI (the server's own name)");
                }
                if (string.IsNullOrWhiteSpace(item.fingerprint))
                {
                    r.Warnings.Add("Reality has no fingerprint set; the default will be used");
                }
            }

            return r;
        }

        /// <summary>
        /// Loose UUID check. Deliberately not strict parsing: real subscriptions do
        /// carry malformed ids, and the point is a clear message, not a new way to
        /// refuse a node that would have worked.
        /// </summary>
        private static bool IsGuidLike(string id)
        {
            var t = id.Trim();
            if (t.Length != 36 || t[8] != '-' || t[13] != '-' || t[18] != '-' || t[23] != '-')
            {
                return false;
            }
            return t.Where(c => c != '-').All(c => Uri.IsHexDigit(c));
        }
    }
}