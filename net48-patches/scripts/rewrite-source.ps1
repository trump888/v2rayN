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
    # DownloaderHelper/DownloadService/ConnectionHandler: strip unsupported
    # HttpClientHandler properties (statement form: anyVar.Prop = value;)
    if ($f.Name -match 'DownloaderHelper|DownloadService|ConnectionHandler|HttpClientHelper') {
        $stripProps = @('MaxConnectionsPerServer', 'PooledConnectionIdleTimeout', 'PooledConnectionLifetime',
                        'EnableMultipleHttp2Connections', 'ConnectTimeout', 'Expect100ContinueTimeout',
                        'KeepAlivePingTimeout', 'KeepAlivePingPolicy', 'SslOptions')
        foreach ($prop in $stripProps) {
            # Statement form: anyVar.Prop = value;
            $content = $content -replace "(?m)^\s*\w+\.$prop\s*=\s*[^;]+;\s*$", "            /* net48: $prop */"
            # anyVar.SslOptions.subProp = value;
            $content = $content -replace "(?m)^\s*\w+\.SslOptions\.[^;]+;\s*$", "            /* net48: SslOptions */"
            # Initializer form: Prop = value, -> delete line
            $content = $content -replace "(?m)^\s*$prop\s*=\s*[^,\r\n]+,?\s*$", ""
        }
        $stripConfigProps = @('BlockTimeout', 'MaxTryAgainOnFailure', 'CustomHttpMessageHandlerFactory')
        foreach ($prop in $stripConfigProps) {
            $content = $content -replace "(?m)^\s*\w+\.$prop\s*=\s*[^;]+;\s*$", "            /* net48: $prop */"
            $content = $content -replace "(?m)^\s*$prop\s*=\s*[^,\r\n]+,?\s*$", ""
        }
        # ConnectTimeout / KeepAliveTimeout in initializer form
        $content = $content -replace "(?m)^\s*ConnectTimeout\s*=\s*[^,;\r\n]+,?\s*$", ""
        $content = $content -replace "(?m)^\s*KeepAliveTimeout\s*=\s*[^,;\r\n]+,?\s*$", ""
        $changed = $true
    }

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

# 19a: GlobalUsings.cs — comment out missing namespaces
$globalUsingsWpf = Join-Path $SourceDir "v2rayN/GlobalUsings.cs"
if (Test-Path $globalUsingsWpf) {
    $content = Get-Content $globalUsingsWpf -Raw -Encoding UTF8
    $changed = $false
    if ($content -match 'System\.Reactive\.Disposables\.Fluent') {
        $content = $content -replace 'global using System\.Reactive\.Disposables\.Fluent;', '// global using System.Reactive.Disposables.Fluent; // net48: not available'
        $changed = $true
    }
    if ($content -match 'ReactiveUI\.Builder') {
        $content = $content -replace 'global using ReactiveUI\.Builder;', '// global using ReactiveUI.Builder; // net48: not available'
        $changed = $true
    }
    if ($changed) {
        [System.IO.File]::WriteAllText($globalUsingsWpf, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "    patched v2rayN/GlobalUsings.cs (commented missing namespaces)"
    }
}

# 19b: HotkeyManager.cs and WindowsUtils.cs — LibraryImport -> DllImport
$csFilesWpf = Get-ChildItem -Path (Join-Path $SourceDir "v2rayN") -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch "\\(obj|bin)\\" }

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

# 19c: SimpleViewLocator.cs — IViewLocator interface mismatch
# ReactiveUI 19.x IViewLocator requires:
#   IViewFor ResolveView<T>(T? viewModel, string? contract)
# Source has:
#   IViewFor<TViewModel>? ResolveView<TViewModel>(string? contract)
# Need to: 1) add viewModel param, 2) change return type to IViewFor? (non-generic)
$simpleView = Join-Path $SourceDir "v2rayN/Common/SimpleViewLocator.cs"
if (Test-Path $simpleView) {
    $content = Get-Content $simpleView -Raw -Encoding UTF8
    if ($content -notmatch 'net48.*IViewLocator') {
        # Change signature: add viewModel param, change return type to IViewFor?,
        # AND remove the "where TViewModel : class" constraint (interface doesn't have it)
        $content = $content -replace
            'public IViewFor<TViewModel>\? ResolveView<TViewModel>\(string\? contract = null\) where TViewModel : class',
            'public IViewFor? ResolveView<TViewModel>(TViewModel? viewModel, string? contract = null)'
        [System.IO.File]::WriteAllText($simpleView, $content, [System.Text.UTF8Encoding]::new($false))
        Write-Host "    patched SimpleViewLocator.cs (IViewLocator signature + return type + constraint)"
    }
}

Write-Host "  Total rewrites: $rewriteCount files touched"
exit 0
