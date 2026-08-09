# =============================================================================
# apply-patches.ps1
# =============================================================================
# Applies all net48 port patches to a fresh checkout of v2rayN upstream.
# Run on a Windows machine (or windows-latest GitHub Actions runner).
#
# Usage:
#   ./apply-patches.ps1 -SourceDir ./v2rayN
#
# Idempotent: safe to re-run; existing patches are skipped.
# =============================================================================
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceDir
)

$ErrorActionPreference = "Stop"
$script:ErrorCount = 0

function Write-Section($msg) {
    Write-Host ""
    Write-Host "=== $msg ===" -ForegroundColor Cyan
}

function Write-Step($msg) {
    Write-Host "  > $msg" -ForegroundColor Green
}

function Write-Warn($msg) {
    Write-Host "  ! $msg" -ForegroundColor Yellow
}

function Write-Err($msg) {
    Write-Host "  X $msg" -ForegroundColor Red
    $script:ErrorCount++
}

# Resolve paths
$SourceDir = (Resolve-Path $SourceDir).Path
$PatchRoot = (Resolve-Path "$PSScriptRoot/..").Path
$ShimDir   = Join-Path $PatchRoot "shims"
$PatchDir  = Join-Path $PatchRoot "patches"

Write-Host "Source:  $SourceDir"
Write-Host "Patches: $PatchRoot"

if (-not (Test-Path (Join-Path $SourceDir "v2rayN.sln"))) {
    Write-Err "v2rayN.sln not found in $SourceDir"
    exit 1
}

# ---------------------------------------------------------------------------
# Step 1: Delete Avalonia desktop project (we only ship WPF)
# ---------------------------------------------------------------------------
Write-Section "Step 1: Remove Avalonia desktop project"
$desktopDir = Join-Path $SourceDir "v2rayN.Desktop"
if (Test-Path $desktopDir) {
    Write-Step "Removing $desktopDir"
    Remove-Item $desktopDir -Recurse -Force
} else {
    Write-Step "Already removed"
}

# ---------------------------------------------------------------------------
# Step 2: Overwrite engineering files
# ---------------------------------------------------------------------------
Write-Section "Step 2: Patch engineering files"

$filesToCopy = @(
    @{ Src = "patches/Directory.Build.props";    Dst = "Directory.Build.props" }
    @{ Src = "patches/Directory.Packages.props"; Dst = "Directory.Packages.props" }
    @{ Src = "patches/v2rayN.sln";               Dst = "v2rayN.sln" }
    @{ Src = "patches/v2rayN.csproj";            Dst = "v2rayN/v2rayN.csproj" }
    @{ Src = "patches/ServiceLib.csproj";        Dst = "ServiceLib/ServiceLib.csproj" }
    @{ Src = "patches/AmazTool.csproj";          Dst = "AmazTool/AmazTool.csproj" }
)

foreach ($f in $filesToCopy) {
    $src = Join-Path $PatchRoot $f.Src
    $dst = Join-Path $SourceDir $f.Dst
    Write-Step "Copy $($f.Src) -> $($f.Dst)"
    Copy-Item $src $dst -Force
}

# ---------------------------------------------------------------------------
# Step 3: Drop shim files
# ---------------------------------------------------------------------------
Write-Section "Step 3: Install polyfill shim files"

$shims = @(
    @{ Src = "shims/IsExternalInit.cs";         Dst = "ServiceLib/Common/IsExternalInit.cs" }
    @{ Src = "shims/SupportedOSPlatform.cs";    Dst = "ServiceLib/Common/SupportedOSPlatform.cs" }
    @{ Src = "shims/BclPolyfills.cs";           Dst = "ServiceLib/Common/BclPolyfills.cs" }
    @{ Src = "shims/BclPolyfills2.cs";          Dst = "ServiceLib/Common/BclPolyfills2.cs" }
    @{ Src = "shims/RxSchedulers.cs";           Dst = "ServiceLib/Common/RxSchedulers.cs" }
    @{ Src = "shims/BinaryPrimitives.cs";       Dst = "ServiceLib.UdpTest/BinaryPrimitives.cs" }
)

foreach ($s in $shims) {
    $src = Join-Path $PatchRoot $s.Src
    $dst = Join-Path $SourceDir $s.Dst
    $dstDir = Split-Path $dst -Parent
    if (-not (Test-Path $dstDir)) {
        New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
    }
    Write-Step "Copy $($s.Src) -> $($s.Dst)"
    Copy-Item $src $dst -Force
}

# ---------------------------------------------------------------------------
# Step 4: Run code rewriting (RxSchedulers, nint.Zero, char args, etc.)
# ---------------------------------------------------------------------------
Write-Section "Step 4: Run automated code rewrites"
$rewriter = Join-Path $PatchRoot "scripts/rewrite-source.ps1"
& $rewriter -SourceDir $SourceDir
if ($LASTEXITCODE -ne 0) {
    Write-Err "Source rewriting failed"
    exit 1
}

# ---------------------------------------------------------------------------
# Step 5: Patch specific files manually (SpeedtestService, DownloaderHelper, etc.)
# ---------------------------------------------------------------------------
Write-Section "Step 5: Apply targeted source patches"
$targetedPatcher = Join-Path $PatchRoot "scripts/patch-targeted.ps1"
& $targetedPatcher -SourceDir $SourceDir
if ($LASTEXITCODE -ne 0) {
    Write-Err "Targeted patching failed"
    exit 1
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Section "Done"
Write-Host "  Errors: $script:ErrorCount"
if ($script:ErrorCount -gt 0) {
    exit 1
}

Write-Host ""
Write-Host "Next steps:"
Write-Host "  cd $SourceDir"
Write-Host "  dotnet restore v2rayN.sln"
Write-Host "  msbuild v2rayN.sln -p:Configuration=Release -p:TargetFramework=net48"
Write-Host ""
