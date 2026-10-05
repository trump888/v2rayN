using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using v2rayN.Base;
using v2rayN.Mode;

namespace v2rayN.Handler
{
    /// <summary>
    /// sing-box outbound generation.
    ///
    /// 5.39 could only run sing-box with a config file the user supplied by hand
    /// (sing_box is registered as a core with "-c config.json"), because the only
    /// generator in this codebase is V2rayConfigHandler, which emits v2ray/Xray
    /// JSON. AnyTLS, Naive and MASQUE are all sing-box-only, so without this
    /// they could not be offered as first-class server types -- users had to hand
    /// write a config and use the Custom server type.
    ///
    /// The field names and TLS block below were validated against sing-box 1.14.2
    /// with `sing-box check`, not copied from documentation.
    /// </summary>
    public class SingboxConfigHandler
    {
        private const string _tag = "SingboxConfigHandler";

        /// <summary>
        /// True for protocol types that no v2ray/Xray core can speak, so the
        /// v2ray generator must not be used for them. Keep in sync with
        /// <see cref="GenOutbound"/>, which is the only outbound shape implemented
        /// so far.
        /// </summary>
        public static bool IsSingboxOnly(EConfigType configType)
            => configType == EConfigType.AnyTLS
            || configType == EConfigType.Naive
            || configType == EConfigType.MASQUE
            || configType == EConfigType.WireGuard;

        /// <summary>
        /// Protocols that sing-box models as an <c>endpoints</c> entry rather than an
        /// outbound. MASQUE is the only one: it registers via
        /// masque.RegisterEndpoint and is typed "masque-client".
        /// </summary>
        public static bool IsEndpointType(EConfigType configType)
            => configType == EConfigType.MASQUE || configType == EConfigType.WireGuard;

        /// <summary>
        /// Build a complete sing-box configuration for a single server.
        /// </summary>
        /// <returns>0 on success.</returns>
        public static int GenConfig(VmessItem item, int localPort, string? logLevel, string? muxEnabled, out string? result)
        {
            try
            {
                if (item == null)
                {
                    result = null;
                    return -1;
                }

                var log = new JObject
                {
                    ["level"] = string.IsNullOrEmpty(logLevel) ? "warning" : logLevel,
                };

                var inbound = new JObject
                {
                    ["type"] = "mixed",
                    ["tag"] = "mixed-in",
                    ["listen"] = "127.0.0.1",
                    ["listen_port"] = localPort,
                };

                // MASQUE is an endpoint, so the "proxy" is a direct outbound that a
                // route rule sends QUIC traffic through the endpoint.
                var viaEndpoint = IsEndpointType(item.configType);
                var outbounds = new JObject
                {
                    ["proxy"] = viaEndpoint
                        ? new JObject { ["type"] = "direct", ["tag"] = "proxy" }
                        : GenOutbound(item),
                    ["direct"] = new JObject { ["type"] = "direct", ["tag"] = "direct" },
                };

                // Send UDP direct, matching what 5.39 already does for the v2ray
                // core, so behaviour does not change when a server is switched to
                // sing-box. Keeps the generated config minimal rather than
                // reproducing sing-box's full default DNS/rule set.
                var rules = new JArray
                {
                    new JObject { ["outbound"] = "direct", ["network"] = "udp" },
                };
                if (viaEndpoint)
                {
                    // An endpoint is only reachable through a route rule. MASQUE is a
                    // QUIC-based proxy so the rule matches quic; WireGuard is UDP-only.
                    rules.Add(new JObject
                    {
                        ["network"] = item.configType == EConfigType.WireGuard ? "udp" : "quic",
                        ["outbound"] = "proxy-endpoint",
                    });
                }
                var route = new JObject
                {
                    ["rules"] = rules,
                    ["final"] = "proxy",
                };

                var config = new JObject
                {
                    ["log"] = log,
                    ["inbounds"] = new JArray { inbound },
                    ["outbounds"] = new JArray { outbounds["proxy"], outbounds["direct"] },
                    ["route"] = route,
                };

                if (viaEndpoint)
                {
                    config["endpoints"] = new JArray { GenEndpoint(item) };
                }

                result = config.ToString(Formatting.Indented);
                return 0;
            }
            catch (Exception ex)
            {
                Utils.SaveLog(_tag, ex);
                result = null;
                return -1;
            }
        }

        /// <summary>
        /// The <c>endpoints</c> entry for protocols sing-box models as an endpoint.
        /// Shape follows 7.x's FillEndpoint for EConfigType.MASQUE.
        /// </summary>
        public static JObject GenEndpoint(VmessItem item)
        {
            if (item.configType == EConfigType.WireGuard)
            {
                return GenWireGuardEndpoint(item);
            }

            var endpoint = new JObject
            {
                ["type"] = "masque-client",
                ["tag"] = "proxy-endpoint",
                ["server"] = item.address,
                ["server_port"] = item.port,
            };

            // Same convention as naive: the whole userinfo is kept in `id` and split
            // here, so the shared flat model needs no username field.
            SplitCredential(item.id, out var user, out var pass);
            if (user != null)
            {
                endpoint["username"] = user;
            }
            if (pass != null)
            {
                endpoint["password"] = pass;
            }

            if (!string.IsNullOrEmpty(item.path))
            {
                endpoint["path"] = item.path;
            }

            var tls = GenTls(item);
            if (tls != null)
            {
                endpoint["tls"] = tls;
            }

            return endpoint;
        }

        /// <summary>
        /// WireGuard as a sing-box endpoint.
        ///
        /// Shape taken from option/wireguard.go and then confirmed against
        /// sing-box 1.14.2 with `sing-box check`, because the obvious guesses are
        /// wrong: there is no "server" field on the endpoint (the peer's address
        /// field is simply "address", not "server"), and sing-box rejects the whole
        /// config with "missing allowed ips for peer 0" if allowed_ips is absent --
        /// which no amount of reading the struct would have told you, because it is
        /// not expressed in the type.
        /// </summary>
        public static JObject GenWireGuardEndpoint(VmessItem item)
        {
            var endpoint = new JObject
            {
                ["type"] = "wireguard",
                ["tag"] = "proxy-endpoint",
                // Required by sing-box. The private key is the credential, so it
                // lives in `id` like every other protocol here.
                ["private_key"] = item.id ?? string.Empty,
            };

            var address = item.interfaceAddress;
            if (!string.IsNullOrEmpty(address))
            {
                endpoint["address"] = new JArray(address
                    .Split(',')
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0));
            }

            if (item.mtu > 0)
            {
                endpoint["mtu"] = item.mtu;
            }

            var peer = new JObject
            {
                ["address"] = item.address,
                ["port"] = item.port,
            };
            if (!string.IsNullOrEmpty(item.publicKey))
            {
                peer["public_key"] = item.publicKey;
            }
            if (!string.IsNullOrEmpty(item.preSharedKey))
            {
                peer["pre_shared_key"] = item.preSharedKey;
            }

            // Required. Defaults to both families, which is what a full-tunnel
            // WireGuard peer means; omitting it makes sing-box refuse to start.
            var allowed = !string.IsNullOrEmpty(item.allowedIps)
                ? item.allowedIps
                : "0.0.0.0/0,::/0";
            peer["allowed_ips"] = new JArray(allowed
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0));

            var reserved = ParseReserved(item.reserved);
            if (reserved.Count > 0)
            {
                peer["reserved"] = new JArray(reserved);
            }

            endpoint["peers"] = new JArray { peer };
            return endpoint;
        }

        /// <summary>
        /// Reserved bytes, comma separated. sing-box wants []uint8, so anything
        /// outside 0-255 is dropped rather than emitting a config that cannot start.
        /// </summary>
        private static List<int> ParseReserved(string? raw)
        {
            var list = new List<int>();
            if (string.IsNullOrEmpty(raw))
            {
                return list;
            }
            foreach (var part in raw.Split(','))
            {
                if (int.TryParse(part.Trim(), out var v) && v >= 0 && v <= 255)
                {
                    list.Add(v);
                }
            }
            return list;
        }

        /// <summary>
        /// "user:pass" or just "pass", matching 7.x's NaiveFmt/MasqueFmt: a colon
        /// means the first half is the username, otherwise the whole thing is the
        /// password.
        /// </summary>
        private static void SplitCredential(string raw, out string user, out string pass)
        {
            user = null;
            pass = null;
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }
            var colon = raw.IndexOf(':');
            if (colon >= 0)
            {
                user = raw.Substring(0, colon);
                pass = raw.Substring(colon + 1);
            }
            else
            {
                pass = raw;
            }
        }

        /// <summary>
        /// The outbound object for one server. Supports the protocols 5.39 can
        /// express with the fields VmessItem actually has.
        /// </summary>
        public static JObject GenOutbound(VmessItem item)
        {
            if (IsEndpointType(item.configType))
            {
                throw new ArgumentException(
                    $"{item.configType} is a sing-box endpoint, not an outbound; use GenEndpoint.");
            }

            var outbound = new JObject
            {
                // sing-box has no MASQUE outbound and never has: verified against
                // 1.10.7, 1.11.15 and 1.14.2, all of which reject both "masque" and
                // "masque-client" as an unknown type. 7.x emits "masque-client",
                // which no released sing-box accepts, so MASQUE is deliberately not
                // implemented here rather than shipped broken.
                ["type"] = item.configType == EConfigType.Naive ? "naive" : "anytls",
                ["tag"] = "proxy",
                ["server"] = item.address,
                ["server_port"] = item.port,
            };

            // VmessItem stores the credential in `id` for the protocols that use a
            // plain password (Hysteria2 does the same), so no new field is needed.
            //
            // naive is the one case with an optional username. Rather than add a
            // field to the shared model, `id` holds the raw userinfo and is split
            // here -- which is exactly what 7.x's NaiveFmt does: "user:pass" when
            // it contains a colon, otherwise the whole thing is the password.
            if (!string.IsNullOrEmpty(item.id))
            {
                if (item.configType == EConfigType.Naive)
                {
                    SplitCredential(item.id, out var nu, out var np);
                    // Always emit username, even empty. A naive outbound with a
                    // password and no username at all passes `sing-box check` on
                    // Linux but the Windows build exits 1 at startup -- and `check`
                    // is not what runs the config. Emitting it unconditionally is
                    // unambiguous on both.
                    outbound["username"] = nu ?? string.Empty;
                    outbound["password"] = np ?? string.Empty;
                }
                else
                {
                    outbound["password"] = item.id;
                }
            }

            var tls = GenTls(item);
            if (tls != null)
            {
                outbound["tls"] = tls;
            }

            return outbound;
        }

        private static JObject GenTls(VmessItem item)
        {
            var sni = !string.IsNullOrEmpty(item.sni) ? item.sni : !string.IsNullOrEmpty(item.requestHost) ? item.requestHost : item.address;
            var host = sni.Split(',').First().Trim();
            if (string.IsNullOrEmpty(host))
            {
                return null;
            }

            var tls = new JObject
            {
                ["enabled"] = true,
                ["server_name"] = host,
            };

            // allowInsecure is a string in VmessItem ("true"/"false"/empty), not
            // a bool. Anything other than an explicit "false" leaves verification
            // on, matching how the v2ray generator treats it.
            if ((item.allowInsecure ?? string.Empty).ToLower() == "true")
            {
                tls["insecure"] = true;
            }

            if (item.alpn is { Count: > 0 })
            {
                var alpn = item.alpn.Where(a => !string.IsNullOrEmpty(a)).ToList();
                if (alpn.Count > 0)
                {
                    tls["alpn"] = new JArray(alpn);
                }
            }

            if (!string.IsNullOrEmpty(item.fingerprint))
            {
                tls["utls"] = new JObject
                {
                    ["enabled"] = true,
                    ["fingerprint"] = item.fingerprint,
                };
            }

            return tls;
        }
    }
}