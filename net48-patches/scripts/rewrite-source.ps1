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

$csFiles = Get-ChildItem -Path $SourceDir -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch "\\(obj|bin)\\" -and $_.FullName -notmatch "\.bak" }

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

    $pattern = '\.Split\(\s*(''\w''|''\\.'')\s*,\s*([A-Za-z_][A-Za-z0-9_.]*)\s*\)'
    while ($content -match $pattern) {
        $content = $content -replace $pattern, ".Split(new[] { `$1 }, `$2)"
        $changed = $true
    }
    $pattern2 = '\.Split\(\s*(''\w''|''\\.'')\s*\)'
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
# Rewrite 10: .Contains('x', StringComparison.X) -> .IndexOf('x', X) >= 0
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $pattern = '\.Contains\(\s*(''\w''|''\\.'')\s*,\s*(StringComparison\.[A-Za-z]+)\s*\)'
    if ($content -match $pattern) {
        $content = $content -replace $pattern, '.IndexOf($1, $2) >= 0'
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched .Contains(char, comp): $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 11: DISABLED — was rewriting arr[1..] to arr.Skip(1).ToArray()
# which breaks STRING slicing (string.Skip(1).ToArray() = char[], not string).
# Let the compiler handle Range natively via our Index/Range polyfills.
# ---------------------------------------------------------------------------
# (old code removed)

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

    # Architecture.RiscV64 / LoongArch64
    if ($content -match 'Architecture\.RiscV64') {
        $content = $content -replace 'Architecture\.RiscV64', 'ArchitecturePolyfills.IsRiscV64'
        $changed = $true
    }
    if ($content -match 'Architecture\.LoongArch64') {
        $content = $content -replace 'Architecture\.LoongArch64', 'ArchitecturePolyfills.IsLoongArch64'
        $changed = $true
    }

    # Enum.IsDefined<T> (generic)
    if ($content -match 'Enum\.IsDefined<') {
        $content = $content -replace 'Enum\.IsDefined<', 'EnumPolyfills.IsDefined<'
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
        $m = [regex]::Match($content, '\.(StartsWith|EndsWith)\(\s*(''\w''|''\\.'')\s*\)')
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
        $m2 = [regex]::Match($content, '\.(StartsWith|EndsWith)\(\s*(''\w''|''\\.'')\s*,\s*(StringComparison\.[A-Za-z]+)\s*\)')
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

Write-Host "  Total rewrites: $rewriteCount files touched"
exit 0
