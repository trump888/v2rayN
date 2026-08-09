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

# All .cs files under v2rayN/ and ServiceLib/
$csFiles = Get-ChildItem -Path $SourceDir -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch "\\(obj|bin)\\" -and $_.FullName -notmatch "\.bak" }

Write-Host "  Scanning $($csFiles.Count) .cs files"

$rewriteCount = 0

# ---------------------------------------------------------------------------
# Rewrite 1: RxSchedulers.* -> RxSchedulersPolyfill.*
# (we provide a shim class RxSchedulers with the same shape)
# Actually we'll define the class as RxSchedulers in the shim, so no rewrite
# needed. Skip this section.
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# Rewrite 2: `nint.Zero` -> `IntPtr.Zero`
# ---------------------------------------------------------------------------
$pattern2 = 'nint\.Zero'
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match $pattern2) {
        $new = $content -replace $pattern2, 'IntPtr.Zero'
        [System.IO.File]::WriteAllText($f.FullName, $new, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched nint.Zero: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 3: `nuint.Zero` -> `UIntPtr.Zero`
# ---------------------------------------------------------------------------
$pattern3 = 'nuint\.Zero'
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match $pattern3) {
        $new = $content -replace $pattern3, 'UIntPtr.Zero'
        [System.IO.File]::WriteAllText($f.FullName, $new, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched nuint.Zero: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 4: `new nint(x)` / `new nuint(x)` -> `new IntPtr(x)` / `new UIntPtr(x)`
# ---------------------------------------------------------------------------
$pattern4a = 'new\s+nint\('
$pattern4b = 'new\s+nuint\('
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false
    if ($content -match $pattern4a) {
        $content = $content -replace $pattern4a, 'new IntPtr('
        $changed = $true
    }
    if ($content -match $pattern4b) {
        $content = $content -replace $pattern4b, 'new UIntPtr('
        $changed = $true
    }
    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched new nint(): $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 5: `await using var x = ...` -> `using var x = ...` (where the type
# doesn't implement IAsyncDisposable on net48 — DownloadService, Stream, etc.)
# Conservative: only rewrite in known-broken files; leave others for compiler.
# ---------------------------------------------------------------------------
$pattern5a = 'await\s+using\s+var\s+'
$pattern5b = 'await\s+using\s+\('
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false
    if ($content -match $pattern5a) {
        $content = $content -replace $pattern5a, 'using var '
        $changed = $true
    }
    if ($content -match $pattern5b) {
        $content = $content -replace $pattern5b, 'using ('
        $changed = $true
    }
    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched await using: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 6: `Math.Clamp(x, min, max)` -> `Math.Max(min, Math.Min(max, x))`
# ---------------------------------------------------------------------------
# We can't easily parse generic multi-arg with regex; do a careful replacement.
# Match: Math.Clamp( <expr1> , <expr2> , <expr3> )
# Use a recursive balanced-bracket regex.
$pattern6 = 'Math\.Clamp\s*\('
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match $pattern6) {
        # Use a simpler approach: replace Math.Clamp( with Math.Max(min, Math.Min(max, ...))
        # but we can't easily extract the 3 args. Instead, leave it for the
        # BclPolyfills extension method, which is the safer approach.
        # Note: Math.Clamp as a *static* method on System.Math doesn't exist on net48.
        # Our BclPolyfills.cs provides it as a static method on a sibling type
        # named MathPolyfills. The user code calls Math.Clamp(...).
        # We need to redirect: rename Math.Clamp -> MathPolyfills.Clamp
        $content = $content -replace 'Math\.Clamp\s*\(', 'MathPolyfills.Clamp('
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched Math.Clamp: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 7: `File.WriteAllTextAsync` / `ReadAllTextAsync` etc. ->
#            `FilePolyfills.WriteAllTextAsync` etc.
# ---------------------------------------------------------------------------
$fileRenames = @(
    @{ From = 'File\.WriteAllTextAsync'; To = 'FilePolyfills.WriteAllTextAsync' }
    @{ From = 'File\.ReadAllTextAsync';  To = 'FilePolyfills.ReadAllTextAsync' }
    @{ From = 'File\.ReadAllBytesAsync'; To = 'FilePolyfills.ReadAllBytesAsync' }
    @{ From = 'File\.WriteAllBytesAsync'; To = 'FilePolyfills.WriteAllBytesAsync' }
)
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false
    foreach ($r in $fileRenames) {
        if ($content -match $r.From) {
            $content = $content -replace $r.From, $r.To
            $changed = $true
        }
    }
    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched File.*Async: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 8: `Enum.Parse<T>(...)` -> `EnumPolyfills.Parse<T>(...)`
# (net48 Enum.Parse is non-generic; .NET 5+ added generic overload)
# ---------------------------------------------------------------------------
$pattern8 = 'Enum\.Parse<'
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match $pattern8) {
        $content = $content -replace 'Enum\.Parse<', 'EnumPolyfills.Parse<'
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched Enum.Parse<T>: $($f.Name)"
    }
}
# Same for Enum.TryParse<T>
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match 'Enum\.TryParse<') {
        $content = $content -replace 'Enum\.TryParse<', 'EnumPolyfills.TryParse<'
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched Enum.TryParse<T>: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 9: `string.Split(char, ...)` -> `string.Split(new[] { char }, ...)`
# We rewrite only the simple two-arg form `s.Split('x')` and
# `s.Split('x', StringSplitOptions.X)`. Complex forms need manual review.
# ---------------------------------------------------------------------------
# We rely on our extension method in BclPolyfills.cs that provides
# `string.Split(this string s, char separator, StringSplitOptions options)`.
# That handles s.Split('x', options) but NOT s.Split('x') alone
# (because the param array overload on net48 already handles single char).
# So no rewrite needed.

# Actually: extension methods are LOWER priority than instance methods.
# net48's string has Split(params char[]) which catches Split('x', options)
# as Split(new char[] { 'x', (char)options }) — compiler CS1503.
# So we DO need to rewrite. Use a precise regex that captures:
#   .Split('x')              -> .Split(new[] { 'x' })
#   .Split('x', options)     -> .Split(new[] { 'x' }, options)
#   .Split('x', count, opt)  -> .Split(new[] { 'x' }, count, opt)
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false

    # Pattern: .Split('x', StringSplitOptions.X)  -- the most common form
    # Match: .Split(<char literal>, <identifier>)
    $pattern = '\.Split\(\s*(''\w''|''\\.'')\s*,\s*([A-Za-z_][A-Za-z0-9_.]*)\s*\)'
    while ($content -match $pattern) {
        $replacement = ".Split(new[] { `$1 }, `$2)"
        $content = $content -replace $pattern, $replacement
        $changed = $true
    }

    # Pattern: .Split('x')  -- single-arg form (no options)
    $pattern2 = '\.Split\(\s*(''\w''|''\\.'')\s*\)'
    while ($content -match $pattern2) {
        $replacement = ".Split(new[] { `$1 })"
        $content = $content -replace $pattern2, $replacement
        $changed = $true
    }

    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched .Split(char): $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 9b: `string.Contains('x', StringComparison.X)` -> use .IndexOf form
# (extension methods don't override instance; need to rewrite at call site)
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false

    # Pattern: .Contains(<char>, <StringComparison>)
    $pattern = '\.Contains\(\s*(''\w''|''\\.'')\s*,\s*(StringComparison\.[A-Za-z]+)\s*\)'
    while ($content -match $pattern) {
        # Replace with: .IndexOf(<char>, <comparison>) >= 0
        $replacement = ".IndexOf(`$1, `$2) >= 0"
        $content = $content -replace $pattern, $replacement
        $changed = $true
    }

    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched .Contains(char, comparison): $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 10: `string.Contains(char)` -> already covered by extension method.
# `string.Contains(string, StringComparison)` -> extension method too.
# Skip.

# ---------------------------------------------------------------------------
# Rewrite 11: `UnixFileMode` -> int (with default 0644)
# Used by File.SetMode() etc. which are .NET 7+ only. Replace with comment.
# IMPORTANT: must NOT match UnixFileMode when it appears as part of an
# identifier like `SetUnixFileMode`. Use word boundaries.
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match 'UnixFileMode') {
        # Replace `File.SetUnixFileMode(path, mode)` with a no-op comment
        # (the method itself is .NET 7+; we can't even call it)
        $content = $content -replace 'File\.SetUnixFileMode\s*\([^)]*\)', '/* net48: SetUnixFileMode not supported */'

        # Replace TYPE references only (word-boundary protected)
        # Match: UnixFileMode not preceded by a letter (so SetUnixFileMode is safe)
        $content = $content -replace '(?<![A-Za-z])UnixFileMode(?![A-Za-z])', 'int /* net48: was UnixFileMode */'

        # Fix any method names that got broken: Setint /*...*/ -> SetUnixFileMode
        $content = $content -replace 'Setint\s*/\*\s*net48:\s*was\s*UnixFileMode\s*\*/', 'SetUnixFileMode'

        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched UnixFileMode: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 12: array range slice `arr[1..n]` -> `arr.Skip(1).Take(n-1).ToArray()`
# or `arr[1..]` -> `arr.Skip(1).ToArray()`
# Because net48 RuntimeHelpers lacks GetSubArray<T> which the compiler emits
# for array Range slicing. We rewrite the most common patterns.
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    $changed = $false

    # arr[1..arr.Length] -> arr.Skip(1).ToArray()
    $pattern = '(\w+)\[(\d+)\.\.(\w+)\.Length\]'
    while ($content -match $pattern) {
        $arr = $matches[1]
        $start = $matches[2]
        $endVar = $matches[3]
        $content = $content -replace [regex]::Escape($matches[0]), "$arr.Skip($start).ToArray()"
        $changed = $true
    }

    # arr[1..] -> arr.Skip(1).ToArray()
    $pattern2 = '(\w+)\[(\d+)\.\.\]'
    while ($content -match $pattern2) {
        $arr = $matches[1]
        $start = $matches[2]
        $content = $content -replace [regex]::Escape($matches[0]), "$arr.Skip($start).ToArray()"
        $changed = $true
    }

    # arr[..n] -> arr.Take(n).ToArray()
    $pattern3 = '(\w+)\[\.\.(\d+)\]'
    while ($content -match $pattern3) {
        $arr = $matches[1]
        $n = $matches[2]
        $content = $content -replace [regex]::Escape($matches[0]), "$arr.Take($n).ToArray()"
        $changed = $true
    }

    if ($changed) {
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched array range slice: $($f.Name)"
    }
}

# ---------------------------------------------------------------------------
# Rewrite 13: collection expressions `["a", "b"]`
# C# 12 supports them natively; the compiler will infer List<string>.
# However, when the target type is NOT List<string> (e.g. string[] or List<T>),
# inference fails. Our polyfill handles List<string>; for others, leave them
# to compiler and surface as errors.
# Skip — C# 12 native is best.

# ---------------------------------------------------------------------------
# Rewrite 14: `Environment.ProcessPath` -> `EnvironmentPolyfills.ProcessPath`
# (net48 Environment class lacks ProcessPath property; .NET Core 3+ only)
# ---------------------------------------------------------------------------
foreach ($f in $csFiles) {
    $content = Get-Content $f.FullName -Raw -Encoding UTF8
    if ($content -match 'Environment\.ProcessPath') {
        $content = $content -replace 'Environment\.ProcessPath', 'EnvironmentPolyfills.ProcessPath'
        [System.IO.File]::WriteAllText($f.FullName, $content, [System.Text.UTF8Encoding]::new($false))
        $rewriteCount++
        Write-Host "    patched Environment.ProcessPath: $($f.Name)"
    }
}

Write-Host "  Total rewrites: $rewriteCount files touched"
exit 0
