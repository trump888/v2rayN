# =============================================================================
# rewrite-source.ps1
# =============================================================================
# Rewrites v2rayN source code patterns that don't compile on net48.
# Idempotent: detects already-rewritten patterns and skips them.
# =============================================================================
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$SourceDir
)

$ErrorActionPreference = "Stop"
$SourceDir = (Resolve-Path $SourceDir).Path

# Constructs this script recognises but cannot rewrite automatically. Each entry
# needs judgement to fix; the script reports them and exits non-zero so CI fails
# loudly instead of emitting a silently broken build.
$script:Unhandled = @()

$csFiles = Get-ChildItem -Path $SourceDir -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch "\\(obj|bin)\\" -and
                   $_.FullName -notmatch "\.bak" -and
                   $_.Name -notmatch "BclPolyfills|Polyfills|IsExternalInit|SupportedOSPlatform|RxSchedulers|BinaryPrimitives" }

Write-Host "  Scanning $($csFiles.Count) .cs files"

$rewriteCount = 0

# ---------------------------------------------------------------------------
# Rewrite 1-3: nint.Zero / nuint.Zero / new nint()
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false
    if ($content -match 'nint\.Zero')      { $content = $content -replace 'nint\.Zero', 'IntPtr.Zero';      $changed = $true }
    if ($content -match 'nuint\.Zero')     { $content = $content -replace 'nuint\.Zero', 'UIntPtr.Zero';   $changed = $true }
    if ($content -match 'new\s+nint\(')    { $content = $content -replace 'new\s+nint\(', 'new IntPtr(';   $changed = $true }
    if ($content -match 'new\s+nuint\(')   { $content = $content -replace 'new\s+nuint\(', 'new UIntPtr('; $changed = $true }
    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched nint: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 4: await using -> using (net48 lacks IAsyncDisposable on many types)
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false
    if ($content -match 'await\s+using\s+var\s+') { $content = $content -replace 'await\s+using\s+var\s+', 'using var '; $changed = $true }
    if ($content -match 'await\s+using\s+\(')     { $content = $content -replace 'await\s+using\s+\(', 'using (';     $changed = $true }
    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched await using: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 5: Math.Clamp -> MathPolyfills.Clamp
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match 'Math\.Clamp\s*\(') {
        $content = $content -replace 'Math\.Clamp\s*\(', 'MathPolyfills.Clamp('
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched Math.Clamp: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 6: File.*Async -> FilePolyfills.*Async
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false
    if ($content -match 'File\.WriteAllTextAsync')  { $content = $content -replace 'File\.WriteAllTextAsync',  'FilePolyfills.WriteAllTextAsync';  $changed = $true }
    if ($content -match 'File\.ReadAllTextAsync')   { $content = $content -replace 'File\.ReadAllTextAsync',   'FilePolyfills.ReadAllTextAsync';   $changed = $true }
    if ($content -match 'File\.ReadAllBytesAsync')  { $content = $content -replace 'File\.ReadAllBytesAsync',  'FilePolyfills.ReadAllBytesAsync'; $changed = $true }
    if ($content -match 'File\.WriteAllBytesAsync') { $content = $content -replace 'File\.WriteAllBytesAsync', 'FilePolyfills.WriteAllBytesAsync'; $changed = $true }
    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched File.*Async: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 7: Enum.Parse<T> -> EnumPolyfills.Parse<T>
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false
    if ($content -match 'Enum\.Parse<')      { $content = $content -replace 'Enum\.Parse<',      'EnumPolyfills.Parse<';      $changed = $true }
    if ($content -match 'Enum\.TryParse<')   { $content = $content -replace 'Enum\.TryParse<',   'EnumPolyfills.TryParse<';   $changed = $true }
    if ($content -match 'Enum\.GetValues<')  { $content = $content -replace 'Enum\.GetValues<',  'EnumPolyfills.GetValues<';  $changed = $true }
    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched Enum.<T>: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 8: UnixFileMode type -> int (word-boundary protected)
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match 'UnixFileMode') {
        $content = $content -replace 'File\.SetUnixFileMode\s*\([^)]*\)', '/* net48: SetUnixFileMode not supported */'
        $content = $content -replace '(?<![A-Za-z])UnixFileMode(?![A-Za-z])', 'int /* net48: was UnixFileMode */'
        $content = $content -replace 'Setint\s*/\*\s*net48:\s*was\s*UnixFileMode\s*\*/', 'SetUnixFileMode'
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched UnixFileMode: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 9: .Split('x', options) -> .Split(new[] { 'x' }, options)
# Extension methods don't override instance; need call-site rewrite.
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false

    $pattern = "\.Split\(\s*('\w'|'\\.')\s*,\s*([A-Za-z_][A-Za-z0-9_.]*)\s*\)"
    while ($content -match $pattern) {
        $content = $content -replace $pattern, ".Split(new[] { `$1 }, `$2)"
        $changed = $true
    }
    $pattern2 = "\.Split\(\s*('\w'|'\\.')\s*\)"
    while ($content -match $pattern2) {
        $content = $content -replace $pattern2, ".Split(new[] { `$1 })"
        $changed = $true
    }
    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched .Split(char): $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 9c: .Split("sep", count) -> .Split(new[] { "sep" }, count, StringSplitOptions.None)
# net48 string.Split doesn't have (string, int) overload
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $pattern = '\.Split\(\s*("[^"]*")\s*,\s*(\d+)\s*\)'
    if ($content -match $pattern) {
        $content = $content -replace $pattern, '.Split(new[] { $1 }, $2, StringSplitOptions.None)'
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched .Split(string, count): $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 9d: int.TryParse(x.AsSpan(n), out var y) -> int.TryParse(x.Substring(n), out var y)
# net48 int.TryParse doesn't accept ReadOnlySpan<char>
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $pattern = 'int\.TryParse\((\w+)\.AsSpan\((\w+)\)\s*,'
    if ($content -match $pattern) {
        $content = $content -replace $pattern, 'int.TryParse($1.Substring($2),'
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched int.TryParse(AsSpan): $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 10: .Contains('x', StringComparison.X) -> .IndexOf('x', X) >= 0
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $pattern = "\.Contains\(\s*('\w'|'\\.')\s*,\s*(StringComparison\.[A-Za-z]+)\s*\)"
    if ($content -match $pattern) {
        $content = $content -replace $pattern, '.IndexOf($1, $2) >= 0'
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched .Contains(char, comp): $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 11: array range slice arr[1..n] -> arr.Skip(1).ToArray()
# ONLY rewrite patterns that are clearly arrays:
#   - arr[1..arr.Length]  (uses .Length, almost certainly array not string)
# Do NOT rewrite:
#   - arr[1..]            (could be string, would break with Skip().ToArray())
#   - arr[..n]            (same)
#   - arr[..^1]           (handled by Rewrite 16)
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false

    # arr[1..arr.Length] -> arr.Skip(1).ToArray()
    # This pattern only matches arrays (string uses .Length too but the
    # result of string[1..Length] is a string, not char[]).
    # Unfortunately we can't distinguish string from array at regex level.
    # Compromise: rewrite ALL [n..var.Length] to Skip(n).ToArray(), accept
    # that string[1..str.Length] will break (rare pattern).
    $pattern = '(\w+)\[(\d+)\.\.(\w+)\.Length\]'
    while ($content -match $pattern) {
        $arr = $matches[1]; $start = $matches[2]
        $content = $content -replace [regex]::Escape($matches[0]), "$arr.Skip($start).ToArray()"
        $changed = $true
    }

    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched array range: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 12: Static-class API redirects (the big one)
#   Environment.ProcessPath -> EnvironmentPolyfills.ProcessPath
#   OperatingSystem.Is*()   -> OperatingSystemPolyfills.Is*()
#   ArgumentNullException.ThrowIfNull -> ArgumentNullExceptionPolyfills.ThrowIfNull
#   ArgumentException.ThrowIfNullOrEmpty/WhiteSpace -> ArgumentExceptionPolyfills.*
#   MD5/SHA256/SHA1.HashData -> HashAlgorithmStaticPolyfills.*HashData
#   File.AppendAllTextAsync -> FileAppendPolyfills.AppendAllTextAsync
#   CompressionLevel.SmallestSize -> CompressionLevelPolyfills.SmallestSize
#   StringSplitOptions.TrimEntries -> StringSplitOptionsPolyfillsStatic.TrimEntries
#   MediaTypeNames.Application.Json -> MediaTypeNamesApplicationPolyfills.Json
#   Marshal.GetLastPInvokeError -> MarshalPolyfills.GetLastPInvokeError
#   Convert.TryFromBase64String -> ConvertPolyfills.TryFromBase64String
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false

    if ($content -match 'Environment\.ProcessPath') {
        $content = $content -replace 'Environment\.ProcessPath', 'EnvironmentPolyfills.ProcessPath'
        $changed = $true
    }
    if ($content -match 'OperatingSystem\.IsWindows\(\)')  { $content = $content -replace 'OperatingSystem\.IsWindows\(\)',  'OperatingSystemPolyfills.IsWindows()';  $changed = $true }
    if ($content -match 'OperatingSystem\.IsLinux\(\)')    { $content = $content -replace 'OperatingSystem\.IsLinux\(\)',    'OperatingSystemPolyfills.IsLinux()';    $changed = $true }
    if ($content -match 'OperatingSystem\.IsMacOS\(\)')    { $content = $content -replace 'OperatingSystem\.IsMacOS\(\)',    'OperatingSystemPolyfills.IsMacOS()';    $changed = $true }
    if ($content -match 'OperatingSystem\.IsWindowsVersion') { $content = $content -replace 'OperatingSystem\.IsWindowsVersion', 'OperatingSystemPolyfills.IsWindowsVersion'; $changed = $true }

    if ($content -match 'ArgumentNullException\.ThrowIfNull') {
        $content = $content -replace 'ArgumentNullException\.ThrowIfNull', 'ArgumentNullExceptionPolyfills.ThrowIfNull'
        $changed = $true
    }
    if ($content -match 'ArgumentException\.ThrowIfNullOrEmpty') {
        $content = $content -replace 'ArgumentException\.ThrowIfNullOrEmpty', 'ArgumentExceptionPolyfills.ThrowIfNullOrEmpty'
        $changed = $true
    }
    if ($content -match 'ArgumentException\.ThrowIfNullOrWhiteSpace') {
        $content = $content -replace 'ArgumentException\.ThrowIfNullOrWhiteSpace', 'ArgumentExceptionPolyfills.ThrowIfNullOrWhiteSpace'
        $changed = $true
    }

    if ($content -match 'MD5\.HashData')    { $content = $content -replace 'MD5\.HashData\(',    'HashAlgorithmStaticPolyfills.MD5HashData(';    $changed = $true }
    if ($content -match 'SHA256\.HashData') { $content = $content -replace 'SHA256\.HashData\(', 'HashAlgorithmStaticPolyfills.SHA256HashData('; $changed = $true }
    if ($content -match 'SHA1\.HashData')   { $content = $content -replace 'SHA1\.HashData\(',   'HashAlgorithmStaticPolyfills.SHA1HashData(';   $changed = $true }

    if ($content -match 'File\.AppendAllTextAsync') {
        $content = $content -replace 'File\.AppendAllTextAsync', 'FileAppendPolyfills.AppendAllTextAsync'
        $changed = $true
    }
    if ($content -match 'CompressionLevel\.SmallestSize') {
        $content = $content -replace 'CompressionLevel\.SmallestSize', 'CompressionLevelPolyfills.SmallestSize'
        $changed = $true
    }
    if ($content -match 'StringSplitOptions\.TrimEntries') {
        $content = $content -replace 'StringSplitOptions\.TrimEntries', 'StringSplitOptionsPolyfillsStatic.TrimEntries'
        $changed = $true
    }
    if ($content -match 'MediaTypeNames\.Application\.Json') {
        $content = $content -replace 'MediaTypeNames\.Application\.Json', 'MediaTypeNamesApplicationPolyfills.Json'
        $changed = $true
    }
    if ($content -match 'Marshal\.GetLastPInvokeError') {
        $content = $content -replace 'Marshal\.GetLastPInvokeError', 'MarshalPolyfills.GetLastPInvokeError'
        $changed = $true
    }
    if ($content -match 'Convert\.TryFromBase64String') {
        $content = $content -replace 'Convert\.TryFromBase64String', 'ConvertPolyfills.TryFromBase64String'
        $changed = $true
    }

    # Architecture.RiscV64 / LoongArch64 — comment out the entire case line
    # Pattern: `Architecture.RiscV64 => expr,` -> `// Architecture.RiscV64 => expr, (net48: not supported)`
    # This is safer than trying to replace with a valid Architecture value.
    if ($content -match 'Architecture\.RiscV64') {
        $content = $content -replace '(?m)^\s*Architecture\.RiscV64\s*=>\s*[^,]+,', '// net48: Architecture.RiscV64 case removed'
        $changed = $true
    }
    if ($content -match 'Architecture\.LoongArch64') {
        $content = $content -replace '(?m)^\s*Architecture\.LoongArch64\s*=>\s*[^,]+,', '// net48: Architecture.LoongArch64 case removed'
        $changed = $true
    }

    # Enum.IsDefined<T> (generic)
    if ($content -match 'Enum\.IsDefined<') {
        $content = $content -replace 'Enum\.IsDefined<', 'EnumPolyfills.IsDefined<'
        $changed = $true
    }
    # Enum.IsDefined(value) — non-generic call without type arg
    # ONLY rewrite if NOT followed by `typeof` (i.e. the .NET 5+ single-arg form)
    # Pattern: Enum.IsDefined(notTypeofExpression)
    # Use negative lookahead: Enum.IsDefined(  followed by something that's NOT `typeof`
    $pattern = 'Enum\.IsDefined\((?!typeof)'
    if ($content -match $pattern) {
        $content = $content -replace $pattern, 'EnumPolyfills.IsDefined('
        $changed = $true
    }

    # File.GetUnixFileMode / File.SetUnixFileMode
    if ($content -match 'File\.GetUnixFileMode') {
        $content = $content -replace 'File\.GetUnixFileMode', 'FileUnixModePolyfills.GetUnixFileMode'
        $changed = $true
    }
    if ($content -match 'File\.SetUnixFileMode') {
        $content = $content -replace 'File\.SetUnixFileMode', 'FileUnixModePolyfills.SetUnixFileMode'
        $changed = $true
    }

    # CliWrap BufferedCommandResult.IsSuccess -> IsSuccessPolyfill()
    if ($content -match '\.IsSuccess\b') {
        $content = $content -replace '\.IsSuccess\b', '.IsSuccessPolyfill()'
        $changed = $true
    }

    # Directory.Move(src, dst, overwrite) -> DirectoryPolyfills.Move(src, dst, overwrite)
    # ONLY rewrite if 3-arg form (has overwrite). 2-arg Directory.Move works on net48.
    # We detect by looking for 3 args separated by commas inside the parens.
    # Simple heuristic: if the line has Directory.Move( with 2+ commas in args
    if ($content -match 'Directory\.Move\([^)]*,[^,]*,[^)]*\)') {
        $content = $content -replace 'Directory\.Move\(', 'DirectoryPolyfills.Move('
        $changed = $true
    }

    # File.Move(src, dst, overwrite) -> FileMovePolyfills.Move(src, dst, overwrite)
    # ONLY rewrite if 3-arg form
    if ($content -match 'File\.Move\([^)]*,[^,]*,[^)]*\)') {
        $content = $content -replace 'File\.Move\(', 'FileMovePolyfills.Move('
        $changed = $true
    }

    # X509Certificate2.CreateFromPem -> X509Certificate2Polyfills.CreateFromPem
    if ($content -match 'X509Certificate2\.CreateFromPem') {
        $content = $content -replace 'X509Certificate2\.CreateFromPem', 'X509Certificate2Polyfills.CreateFromPem'
        $changed = $true
    }

    # X509ChainPolicy.TrustMode / CustomTrustStore — in initializer form,
    # DELETE the entire line (extension methods don't work in initializers).
    # Pattern: TrustMode = X509ChainTrustMode.CustomRootTrust,
    if ($content -match 'TrustMode\s*=\s*X509ChainTrustMode') {
        $content = $content -replace "(?m)^\s*TrustMode\s*=\s*X509ChainTrustMode\.\w+,\s*$", "// net48: TrustMode not available in initializer"
        $changed = $true
    }
    # CustomTrustStore.AddRange(...) in statement form — keep (works via extension)
    # But CustomTrustStore = new X509Certificate2Collection() in initializer — delete
    if ($content -match 'CustomTrustStore\s*=\s*new') {
        $content = $content -replace "(?m)^\s*CustomTrustStore\s*=\s*new[^,]+,\s*$", "// net48: CustomTrustStore not available in initializer"
        $changed = $true
    }
    # chainPolicy.CustomTrustStore.AddRange(certs) -> chainPolicy.AddToCustomTrustStore(certs)
    if ($content -match 'CustomTrustStore\.AddRange') {
        $content = $content -replace '(\w+)\.CustomTrustStore\.AddRange\(', '$1.AddToCustomTrustStore('
        $changed = $true
    }
    # chain.ChainElements.Select(...) -> chain.ChainElements.AsEnumerable().Select(...)
    if ($content -match 'ChainElements\.Select') {
        $content = $content -replace 'ChainElements\.Select\(', 'ChainElements.AsEnumerable().Select('
        $changed = $true
    }
    # Chunk(2).Select(c => new string(c)) -> Chunk(2).Select(c => c)
    if ($content -match 'Chunk\(2\)\.Select\(c\s*=>\s*new string\(c\)\)') {
        $content = $content -replace 'Chunk\(2\)\.Select\(c\s*=>\s*new string\(c\)\)', 'Chunk(2).Select(c => c)'
        $changed = $true
    }
    # NOTE: Downloader 3.1.2 / HttpClientHandler member stripping deliberately
    # does NOT live here. It used to, but it (a) deleted MaxTryAgainOnFailure
    # outright instead of renaming it to the 3.x spelling, silently losing
    # download retries, and (b) used `[^;]+;` patterns that can span newlines
    # and swallow the closing `};` of an object initializer. It is now handled
    # once, in patch-targeted.ps1, with line-anchored patterns and a post-check
    # that fails the run if a member could not be removed.

    # string.Join(char, IEnumerable<string>) — net48 only has Join(string, IEnumerable<string>)
    # Convert char literal to string literal: string.Join(',', ... -> string.Join(",", ...
    # Use double-quoted string to avoid PowerShell single-quote escaping issues
    $pattern = "string\.Join\(\s*('[^']')\s*,"
    $m = [regex]::Match($content, $pattern)
    while ($m.Success) {
        $charLit = $m.Groups[1].Value
        $inner = $charLit.Trim("'")
        if ($inner -eq '\\') { $inner = '\\' }
        elseif ($inner.Length -eq 2 -and $inner[0] -eq '\') {
            # keep escape sequences like \n, \t, \r
            $inner = $inner
        }
        $strLit = '"' + $inner + '"'
        $newJoin = "string.Join($strLit,"
        $content = $content.Substring(0, $m.Index) + $newJoin + $content.Substring($m.Index + $m.Length)
        $m = [regex]::Match($content, $pattern)
    }
    if ($content -match 'string\.Join\(\s*"[^"]*"\s*,') {
        $changed = $true
    }

    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched static polyfills: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 13: HttpClient.PatchAsync — already provided as extension method
# in BclPolyfills3.cs, no source rewrite needed.
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# Rewrite 14: SocketsHttpHandler -> HttpClientHandler (anywhere it appears)
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match 'SocketsHttpHandler') {
        $content = $content -replace 'SocketsHttpHandler', 'HttpClientHandler'
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched SocketsHttpHandler: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 15: string.StartsWith('x') / EndsWith('x') -> string.StartsWith("x")
# net48 lacks the char overload; only has string overload.
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $true
    $loops = 0
    while ($changed -and $loops -lt 50) {
        $changed = $false
        $loops++
        # Match .StartsWith('x') or .EndsWith('x')
        $m = [regex]::Match($content, "\.(StartsWith|EndsWith)\(\s*('\w'|'\\.')\s*\)")
        if ($m.Success) {
            $method = $m.Groups[1].Value
            $charLit = $m.Groups[2].Value
            # Convert 'x' to "x"
            $inner = $charLit.Trim("'")
            if ($inner -eq '\\') { $inner = '\\' }
            elseif ($inner.Length -eq 2 -and $inner[0] -eq '\') { $inner = $inner[1] }
            $stringLit = '"' + $inner + '"'
            $newCall = ".$method($stringLit)"
            $content = $content.Substring(0, $m.Index) + $newCall + $content.Substring($m.Index + $m.Length)
            $changed = $true
        }
        # Match .StartsWith('x', StringComparison.X)
        $m2 = [regex]::Match($content, "\.(StartsWith|EndsWith)\(\s*('\w'|'\\.')\s*,\s*(StringComparison\.[A-Za-z]+)\s*\)")
        if ($m2.Success) {
            $method = $m2.Groups[1].Value
            $charLit = $m2.Groups[2].Value
            $comp = $m2.Groups[3].Value
            $inner = $charLit.Trim("'")
            if ($inner -eq '\\') { $inner = '\\' }
            elseif ($inner.Length -eq 2 -and $inner[0] -eq '\') { $inner = $inner[1] }
            $stringLit = '"' + $inner + '"'
            $newCall = ".$method($stringLit, $comp)"
            $content = $content.Substring(0, $m2.Index) + $newCall + $content.Substring($m2.Index + $m2.Length)
            $changed = $true
        }
    }
    if ($loops -gt 1) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched StartsWith/EndsWith(char): $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 16: text[..^1] (Range from end) -> text.Substring(0, text.Length - 1)
# This is a complex pattern; only rewrite the simple form.
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false

    # text[1..]   -> text.Substring(1)             (already handled by Rewrite 11)
    # text[..n]   -> text.Substring(0, n)          (string form, not array)
    # text[..^1]  -> text.Substring(0, text.Length - 1)
    # text[^1..]  -> text.Substring(text.Length - 1)

    # text[..^1] form
    $pattern = '(\w+)\[\.\.\^(\d+)\]'
    while ($content -match $pattern) {
        $s = $matches[1]; $n = $matches[2]
        $content = $content -replace [regex]::Escape($matches[0]), "$s.Substring(0, $s.Length - $n)"
        $changed = $true
    }
    # text[^n..] form
    $pattern2 = '(\w+)\[\^(\d+)\.\.\]'
    while ($content -match $pattern2) {
        $s = $matches[1]; $n = $matches[2]
        $content = $content -replace [regex]::Escape($matches[0]), "$s.Substring($s.Length - $n)"
        $changed = $true
    }
    # text[^a..^b] form (rare)
    # Skip — too complex for regex.

    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched Range-from-end: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 17: null-conditional assignment `obj?.field = value` (CS9260)
# DISABLED: regex is too aggressive, breaks complex expressions like
# `preContext?.IsTunEnabled = true || preContext?.IsTunEnabled == true`
# which becomes invalid C#. The 12 errors of this type need manual review.
# ---------------------------------------------------------------------------
# foreach ($f in $csFiles) {
#     $content = Get-Content $f.FullName -Raw -Encoding UTF8
#     $pattern = '(\w+(?:\.\w+)*)\?\.(\w+)\s*=\s*([^;]+);'
#     if ($content -match $pattern) {
#         $newContent = [regex]::Replace($content, $pattern, 'if ($1 != null) $1.$2 = $3;')
#         if ($newContent -ne $content) {
#             [System.IO.File]::WriteAllText($f.FullName, $newContent, [System.Text.UTF8Encoding]::new($false))
#             $rewriteCount++
#             Write-Host "    patched null-conditional assignment: $($f.Name)"
#         }
#     }
# }

# ---------------------------------------------------------------------------
# Rewrite 18: MaterialDesign 5.x-only XAML attributes — delete them
# ---------------------------------------------------------------------------
$xamlFiles = Get-ChildItem -Path $SourceDir -Recurse -Filter "*.xaml" |
    Where-Object { $_.FullName -notmatch "\\(obj|bin)\\" }

foreach ($f in $xamlFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false

    # Delete materialDesign:NavigationRailAssist.ShowSelectionBackground="True"
    if ($content -match 'NavigationRailAssist\.ShowSelectionBackground') {
        $content = $content -replace '(?m)^\s*materialDesign:NavigationRailAssist\.ShowSelectionBackground="[^"]*"\s*\r?\n', ''
        $changed = $true
    }

    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched XAML NavigationRailAssist: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 19: v2rayN WPF project fixes
#   - Comment out missing namespaces in GlobalUsings.cs
#   - LibraryImport -> DllImport, partial methods -> regular methods
#   - IViewLocator.ResolveView<T> signature fix
# ---------------------------------------------------------------------------

# 19a: GlobalUsings.cs — comment out System.Reactive.Disposables.Fluent
# (we use __DisposeWith in v2rayN.Common namespace, not Fluent.DisposeWith)
$globalUsingsWpf = Join-Path $SourceDir "v2rayN/GlobalUsings.cs"
if (Test-Path $globalUsingsWpf) {
    $content = Get-Content $globalUsingsWpf -Raw -Encoding UTF8
    if ($content -match 'global using System\.Reactive\.Disposables\.Fluent;') {
        $content = $content -replace 'global using System\.Reactive\.Disposables\.Fluent;', '// global using System.Reactive.Disposables.Fluent;'
        [System.IO.File]::WriteAllText($globalUsingsWpf, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "    patched v2rayN/GlobalUsings.cs (commented Fluent namespace)"
    }
}

# Define csFilesWpf early (used by 19a2 and 19b)
$csFilesWpf = Get-ChildItem -Path (Join-Path $SourceDir "v2rayN") -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch "\\(obj|bin)\\" }

# 19a2: Rewrite .DisposeWith( -> .__DisposeWith( in ALL WPF source files
# This avoids any ambiguity with DisposableMixins (different method name)
foreach ($f in $csFilesWpf) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match '\.DisposeWith\(') {
        $content = $content -replace '\.DisposeWith\(', '.__DisposeWith('
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched DisposeWith->__DisposeWith: $($f.Name)"
    }
}

# 19b: HotkeyManager.cs and WindowsUtils.cs — LibraryImport -> DllImport

foreach ($f in $csFilesWpf) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false

    # [LibraryImport("xxx")] -> [DllImport("xxx")]
    if ($content -match '\[LibraryImport\(') {
        $content = $content -replace '\[LibraryImport\(', '[DllImport('
        $changed = $true
    }
    # public static partial int Method(...) -> public static extern int Method(...)
    # ONLY for methods (not classes) — match "static partial" NOT followed by "class"
    if ($content -match 'static\s+partial\s+(?!class)') {
        $content = $content -replace 'static\s+partial\s+(?!class)', 'static extern '
        $changed = $true
    }
    # Also remove "partial" from class declarations: "static partial class" -> "static class"
    if ($content -match 'static\s+partial\s+class') {
        $content = $content -replace 'static\s+partial\s+class', 'static class'
        $changed = $true
    }
    # nint -> IntPtr, nuint -> UIntPtr in WPF project too
    if ($content -match 'nint\b') {
        $content = $content -replace '\bnint\b', 'IntPtr'
        $changed = $true
    }
    if ($content -match 'nuint\b') {
        $content = $content -replace '\bnuint\b', 'UIntPtr'
        $changed = $true
    }

    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched WPF source: $($f.Name)"
    }
}

# 19c: SimpleViewLocator.cs — REMOVED (was a ReactiveUI 19.x workaround).
#
# This used to rewrite
#   public IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract = null) where TViewModel : class
# into a non-generic IViewFor-returning overload, because ReactiveUI 19.x
# declared IViewLocator.ResolveView<T>(T?, string?). Both halves of it are dead
# now, and one of them was actively harmful:
#
#   * The signature pattern stopped matching once upstream dropped the
#     `= null` default and the `new()` constraint from its own declaration, so
#     the signature half silently did nothing.
#   * The body pattern `factory() as IViewFor<TViewModel>` -> `factory() as IViewFor`
#     still matched, so the method kept returning IViewFor<TViewModel> while
#     its body produced a plain IViewFor -> CS0266 in the WPF project.
#
# It is unnecessary in the first place: ReactiveUI 24.3.0's IViewLocator wants
# exactly what upstream already declares, upstream compiles against it on
# net10.0-windows, and the interface is identical across TFMs within the same
# package version. So the file needs no edit at all on net48.

# 19d: app.manifest — declare supportedOS, without which Environment.OSVersion lies
#
# Upstream's app.manifest has no <compatibility> section, which is fine on
# net10.0-windows: .NET 5+ always returns the real OS version. On .NET Framework
# it is not fine — without a supportedOS declaration Environment.OSVersion
# returns 6.2 (Windows 8) no matter what is really running.
#
# That is not cosmetic here. CoreInfoManager.GetCheckUpdateCoreTypes() does:
#
#   if (!(Utils.IsWindows() && Environment.OSVersion.Version.Major < 10))
#       lst.Add(Xray); lst.Add(mihomo); lst.Add(sing_box);
#
# so a hardcoded Major of 6 makes the test false on *every* Windows, and the
# update page silently loses Xray, mihomo and sing-box. It builds clean and
# looks like a missing-feature bug rather than a runtime misreport.
#
# Declaring the OSes the port actually supports (Win7 through Win11) makes
# OSVersion truthful everywhere. Per Microsoft, declaring Windows 10 support
# "will not have any effect when running your app on previous operating
# systems", so listing Windows 10 alongside Windows 7 does not cost Win7 its
# compatibility behaviour — it only tells Windows 10/11 not to lie to us.
#
# Applied to upstream's own manifest rather than shipping a replacement, and
# skipped if a <compatibility> section is already present, so a future upstream
# manifest that declares its own supportedOS list is left alone.
#
# The <application>/<windowsSettings> block in the same pass sets PerMonitorV2
# DPI awareness, which v2rayN.csproj already claims is "enabled via app.manifest
# (already present)" - it was not present, so on .NET Framework the port ran
# DPI-unaware and rendered blurry on scaled displays while upstream on net10.0
# is PerMonitorV2 by default. `true/pm` covers Win8.1+, the 2016-namespace
# dpiAwareness refines it to PerMonitorV2 on Win10+, and Win7 ignores both.
$appManifest = Join-Path $SourceDir "v2rayN/app.manifest"
if (Test-Path $appManifest) {
    $manifest = Get-Content $appManifest -Raw -Encoding UTF8
    if ($manifest -notmatch '<compatibility') {
        $supportedOs = @'
	<application xmlns="urn:schemas-microsoft-com:asm.v3">
		<windowsSettings>
			<dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
			<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2, PerMonitor</dpiAwareness>
		</windowsSettings>
	</application>
	<compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
		<application>
			<!-- Windows 10 / 11 -->
			<supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
			<!-- Windows 8.1 -->
			<supportedOS Id="{1f676c76-80e1-4239-95bb-83d0f6d0da78}" />
			<!-- Windows 8 -->
			<supportedOS Id="{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}" />
			<!-- Windows 7 -->
			<supportedOS Id="{35138b9a-5d96-4fbd-8e2d-a2440225f93a}" />
		</application>
	</compatibility>
</assembly>
'@
        # The manifest ends with </assembly>; append the sections just before it.
        $manifest = $manifest -replace '(?s)</assembly>\s*$', $supportedOs
        [System.IO.File]::WriteAllText($appManifest, $manifest, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched app.manifest (supportedOS + PerMonitorV2 DPI awareness)"
    } else {
        Write-Step "app.manifest already declares <compatibility>; left alone"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 20: [GeneratedRegex] source-generated regex -> cached static Regex
# ---------------------------------------------------------------------------
# .NET 7+ lets a partial Regex factory be stamped with [GeneratedRegex], and
# Roslyn compiles the pattern into IL at build time. net48 has neither the
# attribute nor the generator, so the partial method ends up with no
# implementation part (CS8795).
#
# The attribute arguments are exactly the Regex constructor arguments after the
# pattern, so `[GeneratedRegex(p, o)]` becomes `new Regex(p, o)`. We keep the
# original factory method shape (returning a cached instance) so call sites such
# as `SemVerRegex().Match(x)` are untouched.
#
# RegexOptions.Compiled is deliberately NOT added: GeneratedRegex emits
# interpreted code, and forcing the regex compiler would add JIT cost and
# memory on the resource-constrained Win7/Win10 targets this port targets.
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -notmatch '\[GeneratedRegex\(') { continue }

    $pattern = '(?m)^(?<ind>[ \t]*)\[GeneratedRegex\((?<args>.*)\)\][ \t]*\r?\n' +
               '(?<ind2>[ \t]*)(?<access>private|internal|public|protected)[ \t]+static[ \t]+partial[ \t]+Regex[ \t]+(?<name>\w+)[ \t]*\([ \t]*\)[ \t]*;'
    $m = [regex]::Match($content, $pattern)
    if (-not $m.Success) {
        Write-Host "    ! $($f.Name): [GeneratedRegex] found but declaration shape unrecognised - needs a hand patch"
        $script:Unhandled += "$($f.Name): unrecognised [GeneratedRegex] declaration"
        continue
    }

    $argsText  = $m.Groups['args'].Value
    $name      = $m.Groups['name'].Value
    $ind       = $m.Groups['ind'].Value
    $ind2      = $m.Groups['ind2'].Value
    $access    = $m.Groups['access'].Value
    $fieldName = "__${name}Instance"
    $replacement = @(
        "${ind}// NET48 PORT: was [GeneratedRegex] partial method (net7+ source-generated regex)."
        "${ind}${access} static readonly Regex ${fieldName} = new Regex(${argsText});"
        "${ind2}${access} static Regex ${name}() => ${fieldName};"
    ) -join "`n"

    $content = $content.Substring(0, $m.Index) + $replacement + $content.Substring($m.Index + $m.Length)
    [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
    $rewriteCount++
    Write-Host "    patched GeneratedRegex -> cached Regex: $($f.Name) ($name)"
}

# ---------------------------------------------------------------------------
# Rewrite 21: non-extendable static hosts -> named polyfill class
# ---------------------------------------------------------------------------
# Parallel / Dns / Enum / DecompressionMethods are static classes or enums, so
# the shims cannot hang extension methods off them. Redirect the call sites.
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false

    if ($content -match 'Parallel\.ForEachAsync\b') {
        $content = $content -replace 'Parallel\.ForEachAsync\b', 'ParallelPolyfills.ForEachAsync'
        $changed = $true
    }
    # Dns.GetHostEntryAsync(host, cancellationToken) -> drop the token.
    # net48's Dns has no cancellable overload; the token is checked by the
    # caller before the call, so dropping it preserves behaviour.
    $content = $content -replace 'Dns\.GetHostEntryAsync\(([^,()]+),\s*(?:cancellationToken|ct|token)\)', 'Dns.GetHostEntryAsync($1)'

    foreach ($m in @('GetNames', 'GetName', 'GetFormat')) {
        if ($content -match "Enum\.$m<") {
            $content = $content -replace "Enum\.$m<", "EnumGenericPolyfills.$m<"
            $changed = $true
        }
    }

    # DecompressionMethods.All (.NET 6) does not exist as an enum member on
    # net48; expand it to the two members it was defined as.
    if ($content -match 'DecompressionMethods\.All') {
        $content = $content -replace 'DecompressionMethods\.All', '(DecompressionMethods.GZip | DecompressionMethods.Deflate)'
        $changed = $true
    }

    # HttpRequestHeaders.NonValidated / HttpContentHeaders.NonValidated (.NET 5)
    # are properties on types we cannot extend. Read through the public
    # accessor instead: on net48 there is no restricted-header validation, so
    # the non-validated view and the normal view are the same object.
    if ($content -match '\.NonValidated\b') {
        $content = $content -replace '([\w\.]+)\.NonValidated\b', '$1'
        $changed = $true
    }

    # Numeric span parsing (.NET 8). System.UInt64 is a value type in mscorlib,
    # so `ulong.TryParse(span, out n)` cannot resolve to an extension method.
    # Only rewrite the exact 2-argument span form; string overloads keep working
    # through the real BCL method.
    $content = $content -replace 'ulong\.TryParse\(\s*(\w+)\s*,\s*out\s+(?:var|ulong)?\s*(\w+)\s*\)', 'SpanCharNet8Polyfills.TryParseUInt64($1, out var $2)'
    $content = $content -replace 'long\.TryParse\(\s*(\w+)\s*,\s*out\s+(?:var|long)?\s*(\w+)\s*\)', 'SpanCharNet8Polyfills.TryParseInt64($1, out var $2)'
    if ($content -match 'SpanCharNet8Polyfills\.TryParse') { $changed = $true }

    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched static-host redirect: $($f.Name)"
    }
}

Write-Host "  Total rewrites: $rewriteCount files touched"

if ($script:Unhandled.Count -gt 0) {
    Write-Host ""
    Write-Host "  UNHANDLED net48 incompatibilities (need a manual patch):" -ForegroundColor Yellow
    foreach ($u in $script:Unhandled) {
        Write-Host "    - $u" -ForegroundColor Yellow
    }
    exit 1
}

exit 0
