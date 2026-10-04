// ============================================================================
// Win7Compat.cs -- Windows 7 core-update compatibility for the net48 port
// ============================================================================
// WHY THIS EXISTS
//
// Upstream hides Xray / mihomo / sing-box from the update page whenever
// Environment.OSVersion.Version.Major < 10, because current core builds are Go
// programs and Go 1.21 dropped Windows 7: "Go 1.20 is the last release that will
// run on any release of Windows 7, 8, Server 2008 and Server 2012."
//
// That gate is right about the *default* assets and wrong as an absolute. Two of
// the three projects still ship Win7-capable builds, they are just not the ones
// v2rayN asks for:
//
//   sing-box  .../sing-box-{ver}-windows-amd64-legacy-windows-7.zip
//   mihomo    .../mihomo-windows-amd64-v1-go120-{ver}.zip   (go120 = Go 1.20)
//
// Xray-core publishes no such variant -- a single Xray-windows-64.zip per
// release -- so the last usable one has to be pinned instead. Checked against
// the go.mod of each tag:
//
//   v1.8.3 -> go 1.20   <- last release that runs on Windows 7
//   v1.8.4 -> go 1.21   <- first release that requires Windows 10
//
// Current Xray (v26.x) is built with Go 1.27, so an unpinned update installs a
// binary that cannot start on Win7 at all.
//
// Everything here is a no-op on Windows 10/11, where IsActive is false and
// upstream's own URLs apply unchanged.
//
// TRADEOFF, stated plainly: pinning Xray to v1.8.3 means the Xray on a Win7
// install stays on an early-2024 build and misses every fix since, including
// security fixes. sing-box and mihomo track their latest releases normally.
// ============================================================================

namespace ServiceLib.Common;

internal static class Win7Compat
{
    /// <summary>
    /// Go 1.21 and later require Windows 10 or Windows Server 2016. Anything
    /// below this major version can only run cores built with Go &lt;= 1.20.
    /// </summary>
    public const int LastGoWindowsMajorVersion = 10;

    /// <summary>
    /// Xray v1.8.3 is the last release built with Go 1.20; v1.8.4 moved to Go
    /// 1.21 and requires Windows 10. Xray-core publishes no Win7-specific
    /// asset, so pinning the tag is the only way to get a runnable binary.
    /// </summary>
    public const string XrayLastWin7Tag = "v1.8.3";

    /// <summary>
    /// The mihomo go120 zip contains an exe called
    /// mihomo-windows-amd64-v1-go120.exe, so this name has to be in
    /// CoreInfoManager.GetMihomoCoreExes() as well or the extracted binary is
    /// never found.
    /// </summary>
    public const string MihomoWin7Exe = "mihomo-windows-amd64-v1-go120";

    /// <summary>
    /// True only where the default (latest) core assets cannot run. Depends on
    /// the app.manifest declaring supportedOS -- without it .NET Framework
    /// reports 6.2 for every Windows and this would misfire everywhere.
    /// </summary>
    public static bool IsActive => Utils.IsWindows()
        && Environment.OSVersion.Version.Major < LastGoWindowsMajorVersion;

    /// <summary>
    /// Xray has to be version-pinned rather than URL-swapped. v2rayN resolves
    /// the tag first (UpdateService.GetRemoteVersion) and only then formats the
    /// download URL with it, so the pin has to happen there.
    /// </summary>
    public static bool ShouldPinXray(ECoreType coreType) => IsActive && coreType == ECoreType.Xray;

    /// <summary>
    /// Xray's asset name is the same on Win7 (Xray-windows-64.zip); only the
    /// release tag differs, which XrayLastWin7Tag supplies.
    /// </summary>
    public static string XrayDownloadUrl(string releasesUrl)
        => $"{releasesUrl}/download/{XrayLastWin7Tag}/Xray-windows-64.zip";

    /// <summary>
    /// go120 is mihomo's name for the Go 1.20 build. Note the asset keeps the
    /// upstream "-v1-" infix: mihomo-windows-amd64-v1-go120-{ver}.zip.
    /// </summary>
    public static string MihomoDownloadUrl(string releasesUrl)
        => IsActive
            ? $"{releasesUrl}/download/{{0}}/mihomo-windows-amd64-v1-go120-{{0}}.zip"
            : $"{releasesUrl}/download/{{0}}/mihomo-windows-amd64-v1-{{0}}.zip";

    /// <summary>
    /// sing-box labels its Win7 build "-legacy-windows-7" on the end. The exe
    /// inside is a plain sing-box.exe, so CoreExes needs no change.
    /// </summary>
    public static string SingBoxDownloadUrl(string releasesUrl)
        => IsActive
            ? $"{releasesUrl}/download/{{0}}/sing-box-{{1}}-windows-amd64-legacy-windows-7.zip"
            : $"{releasesUrl}/download/{{0}}/sing-box-{{1}}-windows-amd64.zip";
}