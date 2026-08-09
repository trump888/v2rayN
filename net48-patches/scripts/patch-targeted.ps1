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

    # Stub DoUdpTest method body using regex with balanced braces
    if ($content -match 'private\s+async\s+Task<int>\s+DoUdpTest\s*\(\s*ServerTestItem\s+it\s*\)\s*\{' -and $content -notmatch 'NET48 PORT: ServiceLib\.UdpTest disabled') {
        # Find the method and its body using a simple brace matcher
        $pattern = '(?s)(private\s+async\s+Task<int>\s+DoUdpTest\s*\(\s*ServerTestItem\s+it\s*\)\s*\{)(.*?)(\n    \})'
        $replacement = '$1
        // NET48 PORT: ServiceLib.UdpTest disabled (requires .NET 5+ Stream/Udp APIs).
        await UpdateFunc(it.IndexId, "-1");
        ProfileExManager.Instance.SetTestDelay(it.IndexId, -1);
        return -1;$3'
        $content = [regex]::Replace($content, $pattern, $replacement)
        $changed = $true
    }

    if ($changed) {
        [System.IO.File]::WriteAllText($speedtest, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched SpeedtestService.cs (stubbed DoUdpTest)" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# Patch 3: DownloaderHelper.cs — strip all unsupported HttpClientHandler
# properties and Downloader 5.x-only config properties
# ---------------------------------------------------------------------------
$downloader = Join-Path $SourceDir "ServiceLib/Helper/DownloaderHelper.cs"
if (Test-Path $downloader) {
    $content = Get-Content $downloader -Raw -Encoding UTF8
    if ($content -match 'NET48 PORT: DownloaderHelper stripped' -eq $false) {
        # Replace SocketsHttpHandler -> HttpClientHandler
        $content = $content -replace 'SocketsHttpHandler', 'HttpClientHandler'

        # Remove entire property blocks that HttpClientHandler doesn't support
        # Match any variable name (handler, webRequestHandler, etc.)
        $propsToRemove = @(
            'MaxConnectionsPerServer',
            'PooledConnectionIdleTimeout',
            'PooledConnectionLifetime',
            'EnableMultipleHttp2Connections',
            'ConnectTimeout',
            'Expect100ContinueTimeout',
            'KeepAlivePingTimeout',
            'KeepAlivePingPolicy',
            'SslOptions'
        )
        foreach ($prop in $propsToRemove) {
            # Statement form: anyVar.Prop = value;
            $content = $content -replace "(?m)^\s*\w+\.$prop\s*=\s*[^;]+;\s*$", "            /* net48: $prop not available on HttpClientHandler */"
            # anyVar.SslOptions.SubProp = value;
            $content = $content -replace "(?m)^\s*\w+\.SslOptions\.[^;]+;\s*$", "            /* net48: SslOptions not available */"
        }

        # Remove Downloader 5.x-only config properties
        # Match both `obj.Prop = value;` and `Prop = value,` (object initializer)
        # For initializer form, DELETE THE ENTIRE LINE (including trailing comma)
        $configPropsToRemove = @(
            'BlockTimeout',
            'MaxTryAgainOnFailure',
            'CustomHttpMessageHandlerFactory'
        )
        foreach ($prop in $configPropsToRemove) {
            # Statement form: obj.Prop = value;  -> comment out
            $content = $content -replace "(?m)^\s*\w+\.$prop\s*=\s*[^;]+;\s*$", "            /* net48: $prop not in Downloader 3.0.6 */"
            # Initializer form: Prop = value,  -> DELETE entire line
            $content = $content -replace "(?m)^(\s*)$prop\s*=\s*[^,\r\n]+,\s*$", ""
        }

        # RequestConfiguration.ConnectTimeout (both forms)
        # Initializer form: ConnectTimeout = value  (with or without trailing comma)
        $content = $content -replace "(?m)^\s*ConnectTimeout\s*=\s*[^,;\r\n]+,?\s*$", ""
        # Statement form: obj.ConnectTimeout = value;  -> comment out
        $content = $content -replace "(?m)^\s*\w+\.ConnectTimeout\s*=\s*[^;]+;\s*$", "            /* net48: ConnectTimeout */"
        # KeepAliveTimeout same
        $content = $content -replace "(?m)^\s*KeepAliveTimeout\s*=\s*[^,;\r\n]+,?\s*$", ""
        $content = $content -replace "(?m)^\s*\w+\.KeepAliveTimeout\s*=\s*[^;]+;\s*$", "            /* net48: KeepAliveTimeout */"
        # Also handle handler.PooledConnectionIdleTimeout etc. in statement form
        $extraProps = @('PooledConnectionIdleTimeout', 'PooledConnectionLifetime', 'EnableMultipleHttp2Connections')
        foreach ($prop in $extraProps) {
            $content = $content -replace "(?m)^\s*handler\.$prop\s*=\s*[^;]+;\s*$", "            /* net48: $prop */"
        }

        # Add marker comment
        $content = "// NET48 PORT: DownloaderHelper stripped`n" + $content

        [System.IO.File]::WriteAllText($downloader, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched DownloaderHelper.cs (stripped unsupported properties)" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# Patch 3b: DownloadService.cs — same treatment
# ---------------------------------------------------------------------------
$downloadSvc = Join-Path $SourceDir "ServiceLib/Services/DownloadService.cs"
if (Test-Path $downloadSvc) {
    $content = Get-Content $downloadSvc -Raw -Encoding UTF8
    if ($content -match 'NET48 PORT: DownloadService stripped' -eq $false) {
        $content = $content -replace 'SocketsHttpHandler', 'HttpClientHandler'

        $propsToRemove = @(
            'MaxConnectionsPerServer',
            'PooledConnectionIdleTimeout',
            'PooledConnectionLifetime',
            'EnableMultipleHttp2Connections',
            'ConnectTimeout',
            'SslOptions'
        )
        foreach ($prop in $propsToRemove) {
            # Statement form: anyVar.Prop = value;
            $content = $content -replace "(?m)^\s*\w+\.$prop\s*=\s*[^;]+;\s*$", "            /* net48: $prop not available */"
            $content = $content -replace "(?m)^\s*\w+\.SslOptions\.[^;]+;\s*$", "            /* net48: SslOptions not available */"
            # Initializer form: Prop = value,  -> DELETE entire line
            $content = $content -replace "(?m)^(\s*)$prop\s*=\s*[^,\r\n]+,\s*$", ""
        }
        # ConnectTimeout in initializer form -> DELETE entire line
        $content = $content -replace "(?m)^\s*ConnectTimeout\s*=\s*[^,;\r\n]+,\s*$", ""

        $content = "// NET48 PORT: DownloadService stripped`n" + $content
        [System.IO.File]::WriteAllText($downloadSvc, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched DownloadService.cs (stripped unsupported properties)" -ForegroundColor Green
    }
}

# ---------------------------------------------------------------------------
# Patch 3c: ConnectionHandler.cs — same treatment (has HttpClientHandler.ConnectTimeout)
# ---------------------------------------------------------------------------
$connectionHandler = Join-Path $SourceDir "ServiceLib/Handler/ConnectionHandler.cs"
if (Test-Path $connectionHandler) {
    $content = Get-Content $connectionHandler -Raw -Encoding UTF8
    if ($content -match 'HttpClientHandler' -and $content -notmatch 'NET48 PORT: ConnectionHandler stripped') {
        $propsToRemove = @('ConnectTimeout', 'SslOptions', 'PooledConnectionIdleTimeout', 'PooledConnectionLifetime')
        foreach ($prop in $propsToRemove) {
            $content = $content -replace "(?m)^\s*\w+\.$prop\s*=\s*[^;]+;\s*$", "            /* net48: $prop */"
            $content = $content -replace "(?m)^\s*\w+\.SslOptions\.[^;]+;\s*$", "            /* net48: SslOptions */"
            $content = $content -replace "(?m)^(\s*)$prop\s*=\s*[^,\r\n]+,?\s*$", ""
        }
        $content = "// NET48 PORT: ConnectionHandler stripped`n" + $content
        [System.IO.File]::WriteAllText($connectionHandler, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched ConnectionHandler.cs" -ForegroundColor Green
    }
}
$fileUtils = Join-Path $SourceDir "ServiceLib/Common/FileUtils.cs"
if (Test-Path $fileUtils) {
    $content = Get-Content $fileUtils -Raw -Encoding UTF8
    if ($content -match 'using\s+System\.Formats\.Tar' -and $content -notmatch 'NET48 PORT: System\.Formats\.Tar not available') {
        $new = $content -replace 'using\s+System\.Formats\.Tar\s*;', '// using System.Formats.Tar;  // NET48 PORT: System.Formats.Tar not available'
        $new = $new -replace
            'TarFile\.ExtractToDirectory\(gz,\s*toPath,\s*overwriteFiles:\s*true\);',
            'throw new PlatformNotSupportedException("Tar extraction requires .NET 6+; use external tar.exe on net48.");'
        [System.IO.File]::WriteAllText($fileUtils, $new, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  > Patched FileUtils.cs (stubbed TarFile)" -ForegroundColor Green
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

Write-Host "  Targeted patches applied"
exit 0
