using System;
using System.Collections.Generic;
using v2rayN.Mode;
using System.Linq;

namespace v2rayN.Handler
{
    public sealed class LazyConfig
    {
        private static readonly Lazy<LazyConfig> _instance = new Lazy<LazyConfig>(() => new LazyConfig());
        private Config _config;
        private List<CoreInfo> coreInfos;

        public static LazyConfig Instance => _instance.Value;

        public void SetConfig(ref Config config)
        {
            _config = config;
        }
        public Config GetConfig()
        {
            return _config;
        }

        public List<string> GetShadowsocksSecuritys(VmessItem vmessItem)
        {
            if (GetCoreType(vmessItem, EConfigType.Shadowsocks) == ECoreType.v2fly)
            {
                return Global.ssSecuritys;
            }
            if (GetCoreType(vmessItem, EConfigType.Shadowsocks) == ECoreType.Xray)
            {
                return Global.ssSecuritysInXray;
            }

            return Global.ssSecuritysInSagerNet;
        }

        public ECoreType GetCoreType(VmessItem vmessItem, EConfigType eConfigType)
        {
            if (vmessItem != null && vmessItem.coreType != null)
            {
                return (ECoreType)vmessItem.coreType;
            }

            if (_config.coreTypeItem == null)
            {
                return ECoreType.Xray;
            }
            var item = _config.coreTypeItem.FirstOrDefault(it => it.configType == eConfigType);
            if (item == null)
            {
                return ECoreType.Xray;
            }
            return item.coreType;
        }

        public CoreInfo GetCoreInfo(ECoreType coreType)
        {
            if (coreInfos == null)
            {
                InitCoreInfo();
            }
            return coreInfos.Where(t => t.coreType == coreType).FirstOrDefault();
        }

        private void InitCoreInfo()
        {
            coreInfos = new List<CoreInfo>();

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.v2rayN,
                coreUrl = Global.NUrl,
                coreReleaseApiUrl = Global.NUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                coreDownloadUrl32 = Global.NUrl + "/download/{0}/v2rayN.zip",
                coreDownloadUrl64 = Global.NUrl + "/download/{0}/v2rayN.zip",
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.v2fly,
                coreExes = new List<string> { "wv2ray", "v2ray" },
                arguments = "",
                coreUrl = Global.v2flyCoreUrl,
                coreReleaseApiUrl = Global.v2flyCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                coreDownloadUrl32 = Global.v2flyCoreUrl + "/download/{0}/v2ray-windows-{1}.zip",
                coreDownloadUrl64 = Global.v2flyCoreUrl + "/download/{0}/v2ray-windows-{1}.zip",
                match = "V2Ray",
                versionArg = "-version",
                redirectInfo = true,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.SagerNet,
                coreExes = new List<string> { "SagerNet", "v2ray" },
                arguments = "run",
                coreUrl = Global.SagerNetCoreUrl,
                coreReleaseApiUrl = Global.SagerNetCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                coreDownloadUrl32 = Global.SagerNetCoreUrl + "/download/{0}/v2ray-windows-{1}.zip",
                coreDownloadUrl64 = Global.SagerNetCoreUrl + "/download/{0}/v2ray-windows-{1}.zip",
                match = "V2Ray",
                versionArg = "version",
                redirectInfo = true,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.v2fly_v5,
                coreExes = new List<string> { "v2ray" },
                arguments = "run",
                coreUrl = Global.v2flyCoreUrl,
                coreReleaseApiUrl = Global.v2flyCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                coreDownloadUrl32 = Global.v2flyCoreUrl + "/download/{0}/v2ray-windows-{1}.zip",
                coreDownloadUrl64 = Global.v2flyCoreUrl + "/download/{0}/v2ray-windows-{1}.zip",
                match = "V2Ray",
                versionArg = "version",
                redirectInfo = true,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.Xray,
                coreExes = new List<string> { "xray" },
                arguments = "",
                coreUrl = Global.xrayCoreUrl,
                coreReleaseApiUrl = Global.xrayCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                coreDownloadUrl32 = Global.xrayCoreUrl + "/download/{0}/Xray-windows-{1}.zip",
                coreDownloadUrl64 = Global.xrayCoreUrl + "/download/{0}/Xray-windows-{1}.zip",
                match = "Xray",
                versionArg = "-version",
                redirectInfo = true,
            });

            // clash and clash_meta are no longer registered.
            //
            // clash: Dreamacro/clash is 404, deleted outright. Successor is mihomo,
            // which is registered and offered.
            //
            // clash_meta: MetaCubeX/Clash.Meta still redirects to MetaCubeX/mihomo so
            // downloads succeed, but the release no longer contains any
            // Clash.Meta-* executable -- it ships mihomo-windows-amd64.exe -- so no
            // CoreInfo exe name can match. Successor is mihomo, again registered and
            // offered.
            //
            // Both enum values are kept: they are persisted in saved profiles, and
            // GetCoreType resolves an explicit coreType before consulting the table,
            // so an old profile still routes to mihomo-equivalent behaviour rather
            // than failing to find a core at all.

            // ECoreType.mihomo was in Global.coreTypes (so the UI offered it) but had
            // no CoreInfo registered at all -- selecting it could not resolve a core.
            // It replaced clash and clash_meta, so this is now the only Clash-family
            // entry: the repo they were renamed to ships mihomo-windows-amd64.exe, and
            // none of their old Clash.Meta-* exe names can match it.
            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.mihomo,
                coreExes = new List<string> { "mihomo-windows-amd64", "mihomo-windows-386", "mihomo-amd64", "mihomo" },
                arguments = "-f config.json",
                coreUrl = Global.mihomoCoreUrl,
                coreReleaseApiUrl = Global.mihomoCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                coreDownloadUrl32 = Global.mihomoCoreUrl + "/download/{0}/mihomo-windows-386-{0}.zip",
                coreDownloadUrl64 = Global.mihomoCoreUrl + "/download/{0}/mihomo-windows-amd64-{0}.zip",
                match = "v",
                versionArg = "-v",
                redirectInfo = true,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.hysteria,
                coreExes = new List<string> { "hysteria-windows-amd64", "hysteria-windows-386", "hysteria" },
                arguments = "",
                coreUrl = Global.hysteriaCoreUrl,
                coreReleaseApiUrl = Global.hysteriaCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                coreDownloadUrl32 = Global.hysteriaCoreUrl + "/download/{0}/hysteria-windows-386.exe",
                coreDownloadUrl64 = Global.hysteriaCoreUrl + "/download/{0}/hysteria-windows-amd64.exe",
                redirectInfo = true,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.naiveproxy,
                coreExes = new List<string> { "naiveproxy", "naive" },
                arguments = "config.json",
                coreUrl = Global.naiveproxyCoreUrl,
                redirectInfo = false,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.tuic,
                coreExes = new List<string> { "tuic-client", "tuic" },
                arguments = "-c config.json",
                coreUrl = Global.tuicCoreUrl,
                redirectInfo = true,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.hysteria2,
                coreExes = new List<string> { "hysteria-windows-amd64", "hysteria" },
                arguments = "server -c config.yaml",
                coreUrl = Global.hysteria2CoreUrl,
                coreReleaseApiUrl = Global.hysteria2CoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                redirectInfo = true,
            });

            // Ports of 7.x's registrations for the four cores this fork was missing.
            // Asset names were checked against each repository's latest release
            // rather than copied: the repositories were all live, but clash_meta
            // showed that a live repository says nothing about whether the exe name
            // in a CoreInfo still matches what the release actually contains.
            //
            // None of these four has a config generator here or in 7.x -- they are
            // driven by a user-supplied config file (the Custom server type), which
            // is why their Arguments carry a {0} for the config path.
            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.juicity,
                coreExes = new List<string> { "juicity-client", "juicity" },
                // {0} is the config file path, substituted in V2rayHandler.
                arguments = "run -c {0}",
                coreUrl = Global.juicityCoreUrl,
                coreReleaseApiUrl = Global.juicityCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                coreDownloadUrl64 = Global.juicityCoreUrl + "/download/{0}/juicity-windows-x86_64.zip",
                match = "",
                versionArg = "",
                redirectInfo = false,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.brook,
                coreExes = new List<string> { "brook_windows_amd64", "brook" },
                arguments = "{0}",
                coreUrl = Global.brookCoreUrl,
                coreReleaseApiUrl = Global.brookCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                // published as a bare .exe, not a zip
                coreDownloadUrl64 = Global.brookCoreUrl + "/download/{0}/brook_windows_amd64.exe",
                match = "",
                versionArg = "",
                redirectInfo = false,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.overtls,
                coreExes = new List<string> { "overtls-bin", "overtls" },
                arguments = "-r client -c {0}",
                coreUrl = Global.overtlsCoreUrl,
                coreReleaseApiUrl = Global.overtlsCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                // The -win7- variant, not -pc-, because this branch targets Windows 7.
                coreDownloadUrl64 = Global.overtlsCoreUrl + "/download/{0}/overtls-x86_64-win7-windows-msvc.zip",
                match = "",
                versionArg = "",
                redirectInfo = false,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.shadowquic,
                coreExes = new List<string> { "shadowquic" },
                arguments = "-c {0}",
                coreUrl = Global.shadowquicCoreUrl,
                coreReleaseApiUrl = Global.shadowquicCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                coreDownloadUrl64 = Global.shadowquicCoreUrl + "/download/{0}/shadowquic-x86_64-windows.exe",
                match = "",
                versionArg = "",
                redirectInfo = false,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.mieru,
                coreExes = new List<string> { "mieru", "mieru-server" },
                arguments = "server",
                coreUrl = Global.mieruCoreUrl,
                coreReleaseApiUrl = Global.mieruCoreUrl.Replace(@"https://github.com", @"https://api.github.com/repos"),
                redirectInfo = true,
            });

            coreInfos.Add(new CoreInfo
            {
                coreType = ECoreType.sing_box,
                coreExes = new List<string> { "sing-box-client", "sing-box" },
                arguments = "run",
                coreUrl = Global.singboxCoreUrl,
                redirectInfo = true,
            });
        }

    }
}
