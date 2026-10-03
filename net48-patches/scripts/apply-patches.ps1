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

# Shims go to BOTH ServiceLib/Common AND AmazTool/Root because AmazTool has
# its own .cs files that use Index/Range/MemoryClamp etc. and it doesn't
# reference ServiceLib.
$shims = @(
    @{ Src = "shims/IsExternalInit.cs";            Dst = "ServiceLib/Common/IsExternalInit.cs" }
    @{ Src = "shims/SupportedOSPlatform.cs";       Dst = "ServiceLib/Common/SupportedOSPlatform.cs" }
    @{ Src = "shims/Lock.cs";                      Dst = "ServiceLib/Common/Lock.cs" }
    @{ Src = "shims/CodeAnalysisNullability.cs";   Dst = "ServiceLib/Common/CodeAnalysisNullability.cs" }
    @{ Src = "shims/BclPolyfills.cs";              Dst = "ServiceLib/Common/BclPolyfills.cs" }
    @{ Src = "shims/BclPolyfills2.cs";             Dst = "ServiceLib/Common/BclPolyfills2.cs" }
    @{ Src = "shims/BclPolyfills3.cs";             Dst = "ServiceLib/Common/BclPolyfills3.cs" }
    @{ Src = "shims/BclNet6Polyfills.cs";          Dst = "ServiceLib/Common/BclNet6Polyfills.cs" }
    @{ Src = "shims/TarPolyfills.cs";             Dst = "ServiceLib/Common/TarPolyfills.cs" }
    @{ Src = "shims/BinaryPrimitives.cs";          Dst = "ServiceLib.UdpTest/BinaryPrimitives.cs" }
    # WPF-specific polyfills (only for v2rayN project, not AmazTool)
    @{ Src = "shims/WpfPolyfills.cs";              Dst = "v2rayN/WpfPolyfills.cs" }
    @{ Src = "shims/Lock.cs";                      Dst = "v2rayN/Lock.cs" }
    @{ Src = "shims/CodeAnalysisNullability.cs";   Dst = "v2rayN/CodeAnalysisNullability.cs" }
    # AmazTool only needs basic polyfills (no CliWrap, no ReactiveUI, no X509)
    @{ Src = "shims/BclPolyfills.cs";              Dst = "AmazTool/BclPolyfills.cs" }
    @{ Src = "shims/BclPolyfills2.cs";             Dst = "AmazTool/BclPolyfills2.cs" }
    @{ Src = "shims/SupportedOSPlatform.cs";       Dst = "AmazTool/SupportedOSPlatform.cs" }
    @{ Src = "shims/IsExternalInit.cs";            Dst = "AmazTool/IsExternalInit.cs" }
)

# ---------------------------------------------------------------------------
# Stale shims: files that used to be required but are now provided by the
# real dependencies. Leaving them in place would shadow the genuine type
# (ServiceLib has `global using ServiceLib.Common;`) and break the build.
# ---------------------------------------------------------------------------
$obsoleteShims = @(
    # ReactiveUI >= 20 ships its own static RxSchedulers (MainThreadScheduler /
    # TaskpoolScheduler) and no longer depends on the System.Reactive package,
    # so the hand-rolled replacement both duplicates and breaks.
    @{ Path = "ServiceLib/Common/RxSchedulers.cs"; Reason = "built into ReactiveUI 24.x" }
)
foreach ($o in $obsoleteShims) {
    $p = Join-Path $SourceDir $o.Path
    if (Test-Path $p) {
        Write-Step "Remove obsolete shim $($o.Path) ($($o.Reason))"
        Remove-Item $p -Force
    }
}

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
# Step 6: Validate the patched tree
# ---------------------------------------------------------------------------
# The port's failure mode is not "it fails to build" - that is loud and easy.
# It is "a patch silently stopped matching after an upstream refactor", or
# "a required package/asset is missing", which degrade behaviour or crash at
# runtime while still compiling. These checks turn the classes of silent
# regression we know about into a hard failure.
Write-Section "Step 6: Validate patched tree"

$violations = @()

# Upstream ships extra sample/companion projects (Console, Wpf, WinForms,
# AvaloniaApp, GlobalHotKeys.Test) that are not part of v2rayN.sln and are not
# built by this port. Only validate the projects the solution actually builds,
# otherwise the check cries wolf on projects nobody compiles.
$slnPath = Join-Path $SourceDir "v2rayN.sln"
$buildableProjects = @(Get-ChildItem -Path $SourceDir -Filter *.csproj -Recurse -ErrorAction SilentlyContinue)
if (Test-Path $slnPath) {
    # .sln entries use backslashes ("ServiceLib\ServiceLib.csproj"); normalise
    # both sides before comparing.
    $slnText = (Get-Content $slnPath -Raw -Encoding UTF8).Replace('/', '\')
    $inSln = @(Get-ChildItem -Path $SourceDir -Filter *.csproj -Recurse -ErrorAction SilentlyContinue |
        Where-Object {
            $rel = $_.FullName.Substring($SourceDir.Length) -replace '^[\\/]+', ''
            if (-not $rel) { return $false }
            ($slnText.Contains(($rel -replace '/', '\'))) -or ($slnText.Contains($rel))
        })
    if ($inSln.Count -gt 0) {
        $buildableProjects = $inSln
        Write-Host "  (validating $($inSln.Count) solution projects)"
    }
}

# 1. No leftover reference to the deleted Avalonia desktop frontend.
if (@(Get-ChildItem -Path $SourceDir -Filter *.csproj -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'Desktop' }).Count -gt 0) {
    $violations += "v2rayN.Desktop project still present"
}

# 2. ReactiveUI.Fody must be gone: ReactiveUI 24.x dropped the Fody weaver in
#    favour of partial-property source generators, and keeping the package would
#    reintroduce the CS0122 failures. Match the PackageReference itself so the
#    explanatory comments this port writes are not flagged.
$fodyRefs = @($buildableProjects | Select-String -Pattern '<PackageReference[^>]*ReactiveUI\.Fody' -ErrorAction SilentlyContinue)
if ($fodyRefs.Count -gt 0) {
    $violations += "ReactiveUI.Fody still referenced (use ReactiveUI.SourceGenerators)"
}
if (@(Get-ChildItem -Path $SourceDir -Filter FodyWeavers.xml -Recurse -ErrorAction SilentlyContinue).Count -gt 0) {
    $violations += "FodyWeavers.xml still present"
}

# 3. Every source generator we rely on must actually be referenced, otherwise
#    [Reactive] partial properties silently get no implementation.
foreach ($proj in @("ServiceLib\ServiceLib.csproj", "v2rayN\v2rayN.csproj")) {
    $p = Join-Path $SourceDir $proj
    if (-not (Test-Path $p)) { continue }
    $text = Get-Content $p -Raw -Encoding UTF8
    if ($text -notmatch '<PackageReference[^>]*ReactiveUI\.SourceGenerators') {
        $violations += "$proj does not reference ReactiveUI.SourceGenerators"
    }
}

# 4. TargetFramework must be net48 for every project the solution builds. A
#    leftover explicit upstream value would silently override the net48 default
#    that Directory.Build.props sets.
foreach ($proj in $buildableProjects) {
    $text = Get-Content $proj.FullName -Raw -Encoding UTF8
    if ($text -match '<TargetFramework>(?!net48|[\$\(])') {
        $violations += "$($proj.Name) pins a non-net48 TargetFramework"
    }
}

# 5. UdpTest must stay excluded: it targets .NET 5+ Stream/Socket overloads.
$udpRefs = @($buildableProjects |
    Select-String -Pattern 'ProjectReference[^>]*ServiceLib\.UdpTest' -ErrorAction SilentlyContinue |
    Where-Object { $_.Line -notmatch "BuildNet48" })
if ($udpRefs.Count -gt 0) {
    $violations += "ServiceLib.UdpTest is referenced without the BuildNet48 condition"
}

# 6. RuntimeIdentifier must stay set. Repobot.SQLite.Unofficial ships no
#    buildTransitive targets, so without a RID the SDK never copies its
#    runtimes/win-x64/native/e_sqlite3.dll into the output. The app then throws
#    DllNotFoundException from SqliteHelper's constructor during
#    App.OnStartup - and because dispatcher exceptions are swallowed and
#    ShutdownMode is OnExplicitShutdown, the only symptom is a running process
#    with no window. This is the single easiest way to break the build in a way
#    that still compiles, so it gets its own check.
$buildProps = Get-Content (Join-Path $SourceDir "Directory.Build.props") -Raw -Encoding UTF8
if ($buildProps -notmatch '<RuntimeIdentifier>') {
    $violations += "Directory.Build.props has no <RuntimeIdentifier>: native e_sqlite3.dll will be missing (app cannot start)"
}
if ($buildProps -match '<AppendRuntimeIdentifierToOutputPath>\s*<\s*/AppendRuntimeIdentifierToOutputPath>') {
    $violations += "AppendRuntimeIdentifierToOutputPath is disabled: CI output paths must stay flat"
}

# 7. Post-patch sanity: these rewrites are supposed to have consumed the
#    constructs. Comment lines are stripped first, because this port annotates
#    the code it rewrites and those annotations name the very APIs that must be
#    gone from executable code.
$csFiles = Get-ChildItem -Path $SourceDir -Filter *.cs -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }
$codeOnly = @{}
foreach ($f in $csFiles) {
    $stripped = (Get-Content $f.FullName -Encoding UTF8) |
        Where-Object { $_ -notmatch '^\s*//' } |
        ForEach-Object { $_ -replace '//.*$', '' }
    $codeOnly[$f.FullName] = $stripped -join "`n"
}

$postRewriteChecks = @(
    @{ Pattern = 'MaxTryAgainOnFailure';            Message = 'MaxTryAgainOnFailure not renamed to the 3.x spelling' }
    @{ Pattern = 'CustomHttpMessageHandlerFactory'; Message = 'CustomHttpMessageHandlerFactory survived (absent in Downloader 3.1.2)' }
    @{ Pattern = 'MinimumChunkSize';                Message = 'MinimumChunkSize survived (absent in Downloader 3.1.2)' }
    @{ Pattern = 'System\.Formats\.Tar';            Message = 'System.Formats.Tar survived (net7+, shimmed via tar.exe)' }
    @{ Pattern = 'SocketsHttpHandler';               Message = 'SocketsHttpHandler survived (net48 only has HttpClientHandler)' }
    @{ Pattern = 'ReactiveUI\.Fody';                 Message = 'ReactiveUI.Fody survived in executable code' }
)
foreach ($chk in $postRewriteChecks) {
    $real = @($codeOnly.Keys | Where-Object { $codeOnly[$_] -match $chk.Pattern })
    if ($real.Count -gt 0) {
        $sample = (($real | Select-Object -First 3) | ForEach-Object { Split-Path $_ -Leaf }) -join ', '
        $violations += "$($chk.Message) [$sample]"
    }
}

if ($violations.Count -gt 0) {
    Write-Host "  Validation FAILED:" -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
    exit 1
}
Write-Host "  Validation passed" -ForegroundColor Green

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
