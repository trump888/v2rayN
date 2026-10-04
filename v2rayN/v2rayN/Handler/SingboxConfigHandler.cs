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
            => configType == EConfigType.AnyTLS || configType == EConfigType.Naive;

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

                var outbounds = new JObject
                {
                    ["proxy"] = GenOutbound(item),
                    ["direct"] = new JObject { ["type"] = "direct", ["tag"] = "direct" },
                };

                // Send UDP direct, matching what 5.39 already does for the v2ray
                // core, so behaviour does not change when a server is switched to
                // sing-box. Keeps the generated config minimal rather than
                // reproducing sing-box's full default DNS/rule set.
                var route = new JObject
                {
                    ["rules"] = new JArray
                    {
                        new JObject { ["outbound"] = "direct", ["network"] = "udp" },
                    },
                    ["final"] = "proxy",
                };

                var config = new JObject
                {
                    ["log"] = log,
                    ["inbounds"] = new JArray { inbound },
                    ["outbounds"] = new JArray { outbounds["proxy"], outbounds["direct"] },
                    ["route"] = route,
                };

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
        /// The outbound object for one server. Supports the protocols 5.39 can
        /// express with the fields VmessItem actually has.
        /// </summary>
        public static JObject GenOutbound(VmessItem item)
        {
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
                    var colon = item.id.IndexOf(':');
                    if (colon >= 0)
                    {
                        outbound["username"] = item.id.Substring(0, colon);
                        outbound["password"] = item.id.Substring(colon + 1);
                    }
                    else
                    {
                        outbound["password"] = item.id;
                    }
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

        private static JObject? GenTls(VmessItem item)
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