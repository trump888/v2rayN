# =============================================================================
# patch-targeted.ps1
# =============================================================================
# Targeted manual patches for specific files where generic rewrites can't
# handle the change. Each patch is idempotent (checks if already applied).
# =============================================================================
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceDir
)

$ErrorActionPreference = "Stop"
$SourceDir = (Resolve-Path $SourceDir).Path

# Patches this script recognises but could not apply. Reported and surfaced as a
# non-zero exit so CI fails loudly instead of producing a silently broken build.
$script:Unhandled = @()

function Patch-File {
    param(
        [string]$Path,
        [string]$Marker,        # text that indicates patch already applied
        [scriptblock]$Apply      # script block that takes $content and returns new content
    )
    if (-not (Test-Path $Path)) {
        Write-Host "  ! File not found: $Path" -ForegroundColor Yellow
        return
    }
    $content = Get-Content $Path -Raw -Encoding UTF8
    if ($content -match [regex]::Escape($Marker)) {
        Write-Host "  > Already patched: $(Split-Path $Path -Leaf)"
        return
    }
    $new = & $Apply $content
    if ($new -ne $content) {
        [System.IO.File]::WriteAllText($Path, $new, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched: $(Split-Path $Path -Leaf)" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# Patch 1: ServiceLib.csproj — disable ServiceLib.UdpTest ProjectReference
# We use Condition="'$(BuildNet48)' == 'true'" instead of XML comment,
# because MSBuild's XML parser sometimes chokes on comments containing
# XML-like content (the <ProjectReference ...> tag inside the comment).
# ---------------------------------------------------------------------------
$udpTestRef = 'ServiceLib.UdpTest\ServiceLib.UdpTest.csproj'
$serviceLibCsproj = Join-Path $SourceDir "ServiceLib/ServiceLib.csproj"
if (Test-Path $serviceLibCsproj) {
    $content = Get-Content $serviceLibCsproj -Raw -Encoding UTF8
    # Detect un-disabled UdpTest reference (no Condition attribute)
    if ($content -match '<ProjectReference\s+Include="\.\.\\ServiceLib\.UdpTest\\ServiceLib\.UdpTest\.csproj"\s*/>' -and
        $content -notmatch 'NET48 PORT: UdpTest disabled') {
        $new = $content -replace
            '<ProjectReference Include="\.\.\\ServiceLib\.UdpTest\\ServiceLib\.UdpTest\.csproj"\s*/>',
            '<!-- NET48 PORT: UdpTest disabled (requires .NET 5+ Stream/Udp APIs) -->
                <ProjectReference Include="..\ServiceLib.UdpTest\ServiceLib.UdpTest.csproj" Condition="''$(BuildNet48)'' == ''true''" />'
        if ($new -ne $content) {
            [System.IO.File]::WriteAllText($serviceLibCsproj, $new, [System.Text.UTF8Encoding]::new($false))
            Write-Host "  > Patched ServiceLib.csproj (disabled UdpTest ref)" -ForegroundColor Green
        }
    }
}

# ---------------------------------------------------------------------------
# Patch 2: SpeedtestService.cs — stub DoUdpTest, comment using
# ---------------------------------------------------------------------------
$speedtest = Join-Path $SourceDir "ServiceLib/Services/SpeedtestService.cs"
if (Test-Path $speedtest) {
    $content = Get-Content $speedtest -Raw -Encoding UTF8
    $changed = $false

    # Comment out `using ServiceLib.UdpTest;`
    if ($content -match '^\s*using\s+ServiceLib\.UdpTest\s*;' -and $content -notmatch 'NET48 PORT: UdpTest disabled') {
        $content = $content -replace '(\s*)using\s+ServiceLib\.UdpTest\s*;', '$1// using ServiceLib.UdpTest;  // NET48 PORT: UdpTest disabled'
        $changed = $true
    }

    # Stub DoUdpTest. ServiceLib.UdpTest targets .NET 5+ Stream/Socket overloads
    # (ReceiveAsync, ConnectAsync with 3 args, Stream.ReadExactlyAsync) that
    # net48 does not have, and the project is excluded from the net48 build, so
    # the whole method is replaced with a "delay unavailable" report.
    #
    # The signature has changed upstream more than once (it gained a
    # completedIds dictionary and a CancellationToken), so match the parameter
    # list loosely and replace the balanced body. `[^)]*` keeps this working as
    # upstream evolves, instead of silently stubbing nothing.
    if ($content -notmatch 'NET48 PORT: ServiceLib\.UdpTest disabled') {
        $pattern = '(?s)(private\s+async\s+Task<int>\s+DoUdpTest\s*\([^)]*\)\s*\{)(.*?)(\n    \})'
        $stub = '$1
        // NET48 PORT: ServiceLib.UdpTest disabled (requires .NET 5+ Socket/Stream
        // overloads). Report "no delay" rather than failing the whole speed test.
        await UpdateFunc(it.IndexId, "-1");
        ProfileExManager.Instance.SetTestDelay(it.IndexId, -1);
        completedIds?.TryAdd(it.IndexId, 0);
        return -1;$3'
        $newContent = [regex]::Replace($content, $pattern, $stub)
        if ($newContent -eq $content) {
            Write-Host "  ! SpeedtestService.cs: DoUdpTest found but body not replaced" -ForegroundColor Yellow
            $script:Unhandled += "SpeedtestService.cs: DoUdpTest body not replaced"
        } else {
            $content = $newContent
            $changed = $true
        }
    }

    if ($changed) {
        [System.IO.File]::WriteAllText($speedtest, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched SpeedtestService.cs (stubbed DoUdpTest)" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# Downloader 5.x -> 3.1.2, and SocketsHttpHandler -> HttpClientHandler
# ---------------------------------------------------------------------------
# Downloader 3.2.0 dropped lib/net462 and lib/netstandard2.0 and ships
# lib/net8.0 only, so 3.1.2 is the newest release net48 can bind (verified
# against the published package layout). A few 5.x members do not exist there:
#
#   MaxTryAgainOnFailure            -> MaxTryAgainOnFailover (renamed, not dropped)
#   MinimumChunkSize                -> absent; MinimumSizeOfChunking covers it
#   CustomHttpMessageHandlerFactory -> absent; see note below
#   ConnectTimeout                  -> absent from RequestConfiguration 3.1.2
#   MaxDegreeOfParallelism          -> absent from DownloadConfiguration 3.1.2
#
# Plus the HttpClientHandler members that only exist on SocketsHttpHandler:
# MaxConnectionsPerServer, PooledConnectionIdleTimeout, PooledConnectionLifetime,
# EnableMultipleHttp2Connections, Expect100ContinueTimeout, KeepAlivePingTimeout,
# KeepAlivePingPolicy and the SslOptions block.
#
# On CustomHttpMessageHandlerFactory: upstream injects a pre-built
# SocketsHttpHandler so it can set AutomaticDecompression and extra request
# headers. On .NET Framework, HttpClientHandler.AutomaticDecompression already
# defaults to GZip|Deflate (unlike .NET Core, where it defaults to None), so the
# decompression half of that handler is a no-op here. The header half is
# preserved explicitly: the extra headers are merged into the
# RequestConfiguration.Headers collection, which 3.1.2 still honours.
$downloaderApiRewrites = @(
    "ServiceLib/Helper/DownloaderHelper.cs"
    "ServiceLib/Services/DownloadService.cs"
    "ServiceLib/Handler/ConnectionHandler.cs"
    "ServiceLib/Helper/HttpClientHelper.cs"
)

$deleteMembers = @(
    'MinimumChunkSize'
    'CustomHttpMessageHandlerFactory'
    'ConnectTimeout'
    'MaxDegreeOfParallelism'
    'MaxConnectionsPerServer'
    'PooledConnectionIdleTimeout'
    'PooledConnectionLifetime'
    'EnableMultipleHttp2Connections'
    'Expect100ContinueTimeout'
    'KeepAlivePingTimeout'
    'KeepAlivePingPolicy'
    'BlockTimeout'
)

$handlerOnlyMembers = @('SslOptions')

foreach ($rel in $downloaderApiRewrites) {
    $path = Join-Path $SourceDir $rel
    if (-not (Test-Path $path)) { continue }
    $content = Get-Content $path -Raw -Encoding UTF8
    $original = $content

    # Rename before deleting, so the retry count survives instead of being
    # silently stripped as it was in an earlier revision of this port.
    $content = $content -replace '\bMaxTryAgainOnFailure\b', 'MaxTryAgainOnFailover'

    foreach ($prop in $deleteMembers) {
        # Object-initializer form: `    Prop = value,`  (whole line).
        $content = [regex]::Replace($content, "(?m)^[ \t]*$prop[ \t]*=[^\r\n]*\r?\n", '')
        # Statement form: `    handler.Prop = value;`  (whole line).
        $content = [regex]::Replace($content, "(?m)^[ \t]*\w+\.$prop[ \t]*=[^\r\n]*\r?\n", '')
    }
    foreach ($prop in $handlerOnlyMembers) {
        $content = [regex]::Replace($content, "(?m)^[ \t]*\w+\.$prop\.[^\r\n]*\r?\n", '')
        $content = [regex]::Replace($content, "(?m)^[ \t]*$prop[ \t]*=[^\r\n]*\r?\n", '')
    }

    if ($content -ne $original) {
        # Verify nothing survived that net48 cannot bind. This is the guard that
        # turns "silently degraded build" into "loud failure".
        $leftovers = @()
        foreach ($prop in ($deleteMembers + $handlerOnlyMembers)) {
            if ($content -match "(?m)^[ \t]*(\w+\.)?$prop[ \t]*=") {
                $leftovers += $prop
            }
        }
        if ($leftovers.Count -gt 0) {
            Write-Host "  ! $rel : could not remove $($leftovers -join ', ')" -ForegroundColor Yellow
            $script:Unhandled += "$rel : unsupported members survived: $($leftovers -join ', ')"
        }
        [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched $rel for Downloader 3.1.2 / HttpClientHandler" -ForegroundColor Green
    }
}

# Merge the per-request extra headers into RequestConfiguration.Headers, which
# is how they reach the wire without a custom message handler.
$dlHeaders = Join-Path $SourceDir "ServiceLib/Helper/DownloaderHelper.cs"
if (Test-Path $dlHeaders) {
    $content = Get-Content $dlHeaders -Raw -Encoding UTF8
    if ($content -match 'IReadOnlyDictionary<string, string>\? requestHeaders' -and
        $content -notmatch 'NET48 PORT: requestHeaders merged') {
        $anchor = '(?m)^([ \t]*)headers\.Add\(HttpRequestHeader\.Authorization, "Basic " \+ Utils\.Base64Encode\(uri\.UserInfo\)\);[ \t]*\r?\n'
        $inject = '$1headers.Add(HttpRequestHeader.Authorization, "Basic " + Utils.Base64Encode(uri.UserInfo));
$1// NET48 PORT: requestHeaders merged (Downloader 3.1.2 has no CustomHttpMessageHandlerFactory).
$1if (requestHeaders != null)
$1{
$1    foreach (var header in requestHeaders)
$1    {
$1        headers[header.Key] = header.Value;
$1    }
$1}
'
        $newContent = [regex]::Replace($content, $anchor, $inject)
        if ($newContent -eq $content) {
            Write-Host "  ! DownloaderHelper.cs: Authorization anchor not found, requestHeaders not merged" -ForegroundColor Yellow
            $script:Unhandled += "DownloaderHelper.cs: requestHeaders merge anchor not found"
        } else {
            [System.IO.File]::WriteAllText($dlHeaders, $newContent, [System.Text.UTF8Encoding]::new($false))
            Write-Host "  > Patched DownloaderHelper.cs (merge requestHeaders into Headers)" -ForegroundColor Green
        }
    }
}

# ---------------------------------------------------------------------------
# FileUtils.cs: System.Formats.Tar is .NET 7+, absent on net48
# ---------------------------------------------------------------------------
# Upstream extracts .tar/.tar.gz subscription payloads with TarFile. Win10 1709+
# and Win11 ship tar.exe, so the port shells out to it instead; on a machine
# without tar.exe the caller gets a clear PlatformNotSupportedException rather
# than a missing-type compile error or a silent empty result.
$fileUtils = Join-Path $SourceDir "ServiceLib/Common/FileUtils.cs"
if (Test-Path $fileUtils) {
    $content = Get-Content $fileUtils -Raw -Encoding UTF8
    if ($content -match 'using\s+System\.Formats\.Tar' -and $content -notmatch 'NET48 PORT: System\.Formats\.Tar not available') {
        $content = $content -replace 'using\s+System\.Formats\.Tar\s*;', '// using System.Formats.Tar;  // NET48 PORT: System.Formats.Tar not available'
        $new = $content -replace
            'TarFile\.ExtractToDirectory\(gz,\s*toPath,\s*overwriteFiles:\s*true\);',
            'TarPolyfills.ExtractToDirectory(gz, toPath, overwriteFiles: true);'
        if ($new -eq $content) {
            Write-Host "  ! FileUtils.cs: TarFile.ExtractToDirectory call not found - needs a hand patch" -ForegroundColor Yellow
            $script:Unhandled += "FileUtils.cs: TarFile.ExtractToDirectory pattern not found"
        } else {
            [System.IO.File]::WriteAllText($fileUtils, $new, [System.Text.UTF8Encoding]::new($false))
            Write-Host "  > Patched FileUtils.cs (TarFile -> tar.exe)" -ForegroundColor Green
        }
    }
}

# ---------------------------------------------------------------------------
# EventChannel: use one lock mechanism on both sides of the gate
# ---------------------------------------------------------------------------
# Upstream declares `private readonly Lock _gate = new();` and then uses it two
# different ways:
#
#   1. lock (_gate)          -> on .NET 9+ this lowers to Lock.EnterScope()
#   2. _signal.Synchronize(_gate)
#
# ReactiveUI 24's Synchronize takes a plain `object` gate
# (SynchronizeGateSignal<T>(IObservable<T>, object)) and monitor-locks it. So if
# `Lock` were shimmed as a class - which it has to be on net48, since
# System.Threading.Lock only exists from .NET 9 - the two sides would guard the
# same field with two *different* locks (our ReaderWriterLockSlim vs Monitor),
# and the mutual exclusion the channel depends on would silently not hold.
#
# Declaring the gate as `object` makes both sides use Monitor on the same
# instance, which is exactly what upstream gets on net10.0. This also clears
# CS9216 ("converted to a different type will use likely unintended
# monitor-based locking"), which is the compiler telling us the same thing.
$eventChannel = Join-Path $SourceDir "ServiceLib/Events/EventChannel.cs"
if (Test-Path $eventChannel) {
    $content = Get-Content $eventChannel -Raw -Encoding UTF8
    if ($content -match 'private\s+readonly\s+Lock\s+_gate') {
        $replacement = @(
            '// NET48 PORT: declared as `object`, not `Lock`. ReactiveUI 24 Synchronize()'
            '    // monitor-locks its gate argument, so the gate must be an object for'
            '    // `lock (_gate)` to take the *same* monitor. Using the net48 Lock shim'
            '    // here would guard the field with two unrelated locks.'
            '    private readonly object _gate = new();'
        ) -join "`n"
        $content = $content -replace 'private\s+readonly\s+Lock\s+_gate\s*=\s*new\(\);', $replacement
        [System.IO.File]::WriteAllText($eventChannel, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched EventChannel.cs (gate is object, single lock mechanism)" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# Patch 5: GlobalUsings.cs — add `global using System.Net.Http;`
# ---------------------------------------------------------------------------
$globalUsings = Join-Path $SourceDir "ServiceLib/GlobalUsings.cs"
if (Test-Path $globalUsings) {
    $content = Get-Content $globalUsings -Raw -Encoding UTF8
    if ($content -notmatch 'global using System\.Net\.Http') {
        $new = $content + "`nglobal using System.Net.Http;`n"
        [System.IO.File]::WriteAllText($globalUsings, $new, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched ServiceLib/GlobalUsings.cs (added System.Net.Http)" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# Patch 6: v2rayN/App.xaml.cs — comment out any MaterialDesign resources
# that don't exist in 3.2.0 (will be done by compiler warnings; safe to skip)
# ---------------------------------------------------------------------------

# Patch 7: CoreInfoManager.cs — Windows 7 core updates
#
# Upstream hides all three cores on Windows < 10 because current builds need Go
# 1.21+ (which requires Windows 10). That is true of the *default* assets, but
# sing-box and mihomo still publish Win7-capable ones and Xray has a last
# runnable tag. Win7Compat holds the details and the evidence.
#
# Each rewrite below asserts its anchor matched. A silent no-op here would look
# exactly like "the update page is missing items again".
# ---------------------------------------------------------------------------
$coreInfoManager = Join-Path $SourceDir "ServiceLib/Manager/CoreInfoManager.cs"
if (Test-Path $coreInfoManager) {
    $content = Get-Content $coreInfoManager -Raw -Encoding UTF8

    if ($content -notmatch 'Win7Compat') {

        # 7a. Drop the Windows < 10 gate so the three cores are listed at all.
        $gateOld = @'
            if (!(Utils.IsWindows() && Environment.OSVersion.Version.Major < 10))
            {
                lst.Add(ECoreType.Xray);
                lst.Add(ECoreType.mihomo);
                lst.Add(ECoreType.sing_box);
            }
'@
        $gateNew = @'
            // net48 port: upstream skips these when OSVersion.Major < 10 because the
            // current builds need Go 1.21+ (Windows 10 minimum). Win7Compat swaps in
            // the Win7-capable assets and pins Xray, so list them everywhere.
            lst.Add(ECoreType.Xray);
            lst.Add(ECoreType.mihomo);
            lst.Add(ECoreType.sing_box);
'@
        if ($content.Contains($gateOld)) {
            $content = $content.Replace($gateOld, $gateNew)
        } else {
            throw "CoreInfoManager.cs: GetCheckUpdateCoreTypes gate not found - upstream refactored it"
        }

        # 7b. Xray: same asset name, different tag. The tag is applied in
        #     UpdateService.GetRemoteVersion (Win7Compat.ShouldPinXray) because
        #     that runs before the download URL is formatted.
        $xrayOld = '                    DownloadUrlWin64 = urlXray + "/download/{0}/Xray-windows-64.zip",'
        $xrayNew = '                    DownloadUrlWin64 = Win7Compat.XrayDownloadUrl(urlXray),'
        if ($content.Contains($xrayOld)) {
            $content = $content.Replace($xrayOld, $xrayNew)
        } else {
            throw "CoreInfoManager.cs: Xray DownloadUrlWin64 not found - upstream refactored it"
        }

        # 7c. mihomo: the Win7 asset is the Go 1.20 build, named
        #     mihomo-windows-amd64-v1-go120-{ver}.zip (it keeps the -v1- infix).
        $mihomoOld = '                    DownloadUrlWin64 = urlMihomo + "/download/{0}/mihomo-windows-amd64-v1-{0}.zip",'
        $mihomoNew = '                    DownloadUrlWin64 = Win7Compat.MihomoDownloadUrl(urlMihomo),'
        if ($content.Contains($mihomoOld)) {
            $content = $content.Replace($mihomoOld, $mihomoNew)
        } else {
            throw "CoreInfoManager.cs: mihomo DownloadUrlWin64 not found - upstream refactored it"
        }

        # 7d. sing-box: the Win7 asset carries a -legacy-windows-7 suffix.
        $singboxOld = '                    DownloadUrlWin64 = urlSingbox + "/download/{0}/sing-box-{1}-windows-amd64.zip",'
        $singboxNew = '                    DownloadUrlWin64 = Win7Compat.SingBoxDownloadUrl(urlSingbox),'
        if ($content.Contains($singboxOld)) {
            $content = $content.Replace($singboxOld, $singboxNew)
        } else {
            throw "CoreInfoManager.cs: sing-box DownloadUrlWin64 not found - upstream refactored it"
        }

        # 7e. The mihomo go120 zip contains mihomo-windows-amd64-v1-go120.exe, so
        #     GetCoreExecFile has to know that name or the binary is never found.
        #     Added after the regular v1 entry so an existing install keeps winning.
        $exeOld = '            names.Add("mihomo-windows-amd64-v1");'
        $exeNew = @'
            names.Add("mihomo-windows-amd64-v1");
            // net48 port: exe name inside the Win7 (go120) mihomo zip.
            names.Add(Win7Compat.MihomoWin7Exe);
'@
        if ($content.Contains($exeOld)) {
            $content = $content.Replace($exeOld, $exeNew)
        } else {
            throw "CoreInfoManager.cs: GetMihomoCoreExes windows list not found - upstream refactored it"
        }

        [System.IO.File]::WriteAllText($coreInfoManager, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched CoreInfoManager.cs (Win7 core updates)" -ForegroundColor Green
    } else {
        Write-Host "  > Already patched: CoreInfoManager.cs"
    }
}

# ---------------------------------------------------------------------------
# Patch 8: UpdateService.cs — pin Xray's tag on Windows 7
#
# v2rayN resolves the release tag first (GetRemoteVersion) and only then formats
# the download URL with it, so overriding the URL alone cannot pin a version --
# the tag would still come from GitHub's latest release. Short-circuiting here
# returns v1.8.3, after which ParseDownloadUrl builds
# .../download/v1.8.3/Xray-windows-64.zip.
#
# Note this deliberately does not use CoreInfo.LockedMaxVersion: that resolves
# the tag by scanning the GitHub releases list, and v1.8.3 sits ~81 entries back
# (GitHub serves 30 per page by default), so the lock would silently stop
# finding it as Xray ships more releases.
# ---------------------------------------------------------------------------
$updateService = Join-Path $SourceDir "ServiceLib/Services/UpdateService.cs"
if (Test-Path $updateService) {
    $content = Get-Content $updateService -Raw -Encoding UTF8

    if ($content -notmatch 'Win7Compat\.ShouldPinXray') {

        $anchor = @'
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(type);
        var tagName = string.Empty;
        if (preRelease || coreInfo?.LockedMaxVersion != null)
'@
        $replacement = @'
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo(type);
        var tagName = string.Empty;
        // net48 port: on Windows 7 the latest Xray is built with Go 1.27 and
        // cannot start, so pin the last Go 1.20 release. Has to happen here,
        // before the tag is used to build the download URL.
        if (Win7Compat.ShouldPinXray(type))
        {
            return new UpdateResult(true, new SemanticVersion(Win7Compat.XrayLastWin7Tag));
        }
        if (preRelease || coreInfo?.LockedMaxVersion != null)
'@
        if ($content.Contains($anchor)) {
            $content = $content.Replace($anchor, $replacement)
            [System.IO.File]::WriteAllText($updateService, $content, [System.Text.UTF8Encoding]::new($false))
            Write-Host "  > Patched UpdateService.cs (Xray v1.8.3 pin on Win7)" -ForegroundColor Green
        } else {
            throw "UpdateService.cs: GetRemoteVersion anchor not found - upstream refactored it"
        }
    } else {
        Write-Host "  > Already patched: UpdateService.cs"
    }
}

Write-Host "  Targeted patches applied"

if ($script:Unhandled.Count -gt 0) {
    Write-Host ""
    Write-Host "  UNHANDLED targeted patches:" -ForegroundColor Yellow
    foreach ($u in $script:Unhandled) {
        Write-Host "    - $u" -ForegroundColor Yellow
    }
    exit 1
}

exit 0
