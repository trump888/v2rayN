using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using v2rayN.Mode;

namespace v2rayN.Handler
{
    /// <summary>
    /// Import a sing-box JSON configuration as v2rayN profiles.
    ///
    /// 5.39 could import a v2ray JSON config, a Clash YAML config and SIP008, but not
    /// a sing-box one -- which is an odd gap on a branch that now *generates* sing-box
    /// configs. 7.x has Handler/Fmt/SingboxFmt for this; this is the equivalent here,
    /// written against the flat VmessItem model rather than 7.x's ProfileItem plus
    /// ProtocolExtraItem.
    ///
    /// Accepts either a whole config (an object with "outbounds", and optionally
    /// "endpoints") or a single outbound object / array of them.
    ///
    /// Credentials land in VmessItem.id, the same convention the share-link parsers
    /// use: a bare password for the protocols that have one, and the whole
    /// "user:pass" userinfo for naive and masque, which SingboxConfigHandler splits
    /// again at generation time. That keeps the model free of a username field.
    /// </summary>
    internal static class SingboxConfigImporter
    {
        /// <summary>
        /// True when the text looks like a sing-box configuration rather than a v2ray
        /// one. The two are easy to tell apart: sing-box outbounds carry "type",
        /// v2ray outbounds carry "protocol".
        /// </summary>
        public static bool LooksLikeSingbox(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            try
            {
                var node = JToken.Parse(text);
                return LooksLikeSingbox(node);
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksLikeSingbox(JToken node)
        {
            if (node is JArray arr)
            {
                return arr.OfType<JObject>().Any(LooksLikeSingbox);
            }
            if (node is not JObject obj)
            {
                return false;
            }
            // Any recognised sing-box marker, at top level or on an outbound/endpoint.
            if (HasOutboundMarker(obj))
            {
                return true;
            }
            foreach (var key in new[] { "outbounds", "endpoints" })
            {
                if (obj[key] is JArray list && list.OfType<JObject>().Any(HasOutboundMarker))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasOutboundMarker(JObject obj)
        {
            var type = Str(obj, "type");
            return type != null && Known.ContainsKey(type);
        }

        /// <summary>
        /// Parse a sing-box config or outbound into profiles. Returns an empty list
        /// for anything unrecognised rather than throwing, because this runs inside
        /// the clipboard import chain where several formats are tried in turn.
        /// </summary>
        public static List<VmessItem> Resolve(string text, string? subRemarks = null)
        {
            var result = new List<VmessItem>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            JToken node;
            try
            {
                node = JToken.Parse(text);
            }
            catch
            {
                return result;
            }

            switch (node)
            {
                case JArray arr:
                    foreach (var child in arr.OfType<JObject>())
                    {
                        result.AddRange(ResolveObject(child, subRemarks));
                    }
                    return result;

                case JObject obj:
                    if (!HasOutboundMarker(obj) && obj["outbounds"] is not JArray && obj["endpoints"] is not JArray)
                    {
                        // A bare object: treat it as a single outbound if it looks like
                        // one, otherwise look inside it.
                        result.AddRange(ResolveObject(obj, subRemarks));
                        return result;
                    }
                    foreach (var key in new[] { "outbounds", "endpoints" })
                    {
                        if (obj[key] is not JArray list)
                        {
                            continue;
                        }
                        foreach (var child in list.OfType<JObject>())
                        {
                            result.AddRange(ResolveObject(child, subRemarks));
                        }
                    }
                    return result;

                default:
                    return result;
            }
        }

        /// <summary>sing-box outbound/endpoint type -> v2rayN config type.</summary>
        private static readonly Dictionary<string, EConfigType> Known = new()
        {
            ["vmess"] = EConfigType.VMess,
            ["vless"] = EConfigType.VLESS,
            ["trojan"] = EConfigType.Trojan,
            ["shadowsocks"] = EConfigType.Shadowsocks,
            ["hysteria"] = EConfigType.Hysteria2,
            ["hysteria2"] = EConfigType.Hysteria2,
            ["tuic"] = EConfigType.TUIC,
            ["anytls"] = EConfigType.AnyTLS,
            ["naive"] = EConfigType.Naive,
            ["masque-client"] = EConfigType.MASQUE,
            ["socks"] = EConfigType.Socks,
            ["http"] = EConfigType.HTTP,
        };

        private static List<VmessItem> ResolveObject(JObject obj, string? subRemarks)
        {
            var result = new List<VmessItem>();
            var type = Str(obj, "type");
            if (type == null || !Known.TryGetValue(type, out var configType))
            {
                return result;
            }

            // skip anything tagged as a helper rather than a proxy: "direct", "block",
            // "dns", and the generated "proxy" tag are not servers a user would keep.
            var tag = Str(obj, "tag");
            if (tag == "direct" || tag == "block" || tag == "dns")
            {
                return result;
            }

            var address = Str(obj, "server");
            if (string.IsNullOrEmpty(address))
            {
                return result;
            }

            var item = new VmessItem
            {
                configType = configType,
                address = address,
                port = (int?)Num(obj, "server_port") ?? 0,
                remarks = Str(obj, "tag") ?? subRemarks ?? type,
            };

            // Credential. vmess/vless use uuid; naive and masque use userinfo;
            // everything else uses password.
            var uuid = Str(obj, "uuid");
            if (!string.IsNullOrEmpty(uuid))
            {
                item.id = uuid;
            }
            else
            {
                var user = Str(obj, "username");
                var password = Str(obj, "password");
                if (!string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password))
                {
                    item.id = user + ":" + password;
                }
                else if (!string.IsNullOrEmpty(password))
                {
                    item.id = password;
                }
                else
                {
                    // shadowsocks carries the method separately and the password as
                    // base64; 5.39 keeps both on the item.
                    item.id = Str(obj, "password") ?? string.Empty;
                }
            }

            var security = Str(obj, "method");
            if (!string.IsNullOrEmpty(security))
            {
                item.security = security;
            }

            var flow = Str(obj, "flow");
            if (!string.IsNullOrEmpty(flow))
            {
                item.flow = flow;
            }

            var path = Str(obj, "path");
            if (!string.IsNullOrEmpty(path))
            {
                item.path = path;
            }

            var network = Str(obj, "network");
            if (!string.IsNullOrEmpty(network))
            {
                item.network = network;
            }

            ApplyTls(obj["tls"] as JObject, item);

            var transport = obj["transport"] as JObject;
            if (transport != null)
            {
                var tType = Str(transport, "type");
                if (!string.IsNullOrEmpty(tType))
                {
                    item.network = item.network.IsNullOrEmpty() ? tType : item.network;
                }
                var host = Str(transport, "host");
                if (!string.IsNullOrEmpty(host))
                {
                    item.requestHost = host;
                }
                var tPath = Str(transport, "path");
                if (!string.IsNullOrEmpty(tPath))
                {
                    item.path = tPath;
                }
            }

            result.Add(item);
            return result;
        }

        private static void ApplyTls(JObject? tls, VmessItem item)
        {
            if (tls == null)
            {
                return;
            }
            if (tls["enabled"] != null && tls["enabled"].Type == JTokenType.Boolean && !tls["enabled"].Value<bool>())
            {
                return;
            }

            var sni = Str(tls, "server_name");
            if (!string.IsNullOrEmpty(sni))
            {
                item.sni = sni;
            }
            var insecure = tls["insecure"];
            if (insecure != null && insecure.Type == JTokenType.Boolean && insecure.Value<bool>())
            {
                item.allowInsecure = "true";
            }
            if (tls["alpn"] is JArray alpn)
            {
                var list = alpn.Select(x => x.ToString())
                               .Where(x => !string.IsNullOrEmpty(x))
                               .ToList();
                if (list.Count > 0)
                {
                    item.alpn = list;
                }
            }
            if (tls["utls"] is JObject utls)
            {
                var fp = Str(utls, "fingerprint");
                if (!string.IsNullOrEmpty(fp))
                {
                    item.fingerprint = fp;
                }
            }
        }

        private static string? Str(JObject obj, string key) => obj[key]?.ToString();

        private static double? Num(JObject obj, string key)
        {
            var t = obj[key];
            if (t == null)
            {
                return null;
            }
            try
            {
                return t.Value<double>();
            }
            catch
            {
                return null;
            }
        }
    }
}