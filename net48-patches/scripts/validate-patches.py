#!/usr/bin/env python3
"""
Semantic-equivalent test of the PowerShell patcher.
Replicates apply-patches.ps1 + rewrite-source.ps1 + patch-targeted.ps1
logic in Python, runs against a fresh v2rayN source copy, and reports
what would be patched.

This validates the patch RULES are correct; the actual CI will use the
PowerShell scripts on windows-latest.
"""
from __future__ import annotations
import re
import shutil
import sys
from pathlib import Path


def main(src_root: str) -> None:
    src = Path(src_root)
    if not src.exists():
        sys.exit(f"not found: {src}")

    # Simulate the 3 PowerShell scripts.
    n_files_changed = 0
    n_files_changed += run_rewrites(src)
    n_files_changed += run_targeted_patches(src)
    print(f"\n[validate] {n_files_changed} files patched")


def run_rewrites(src: Path) -> int:
    """Equivalent of rewrite-source.ps1"""
    print("\n[rewrite-source]")
    n = 0
    cs_files = [p for p in src.rglob("*.cs")
                if "/obj/" not in str(p) and "/bin/" not in str(p)
                and ".bak" not in p.name]

    patterns = [
        # (regex, replacement, label)
        (r'nint\.Zero', 'IntPtr.Zero', 'nint.Zero'),
        (r'nuint\.Zero', 'UIntPtr.Zero', 'nuint.Zero'),
        (r'new\s+nint\(', 'new IntPtr(', 'new nint('),
        (r'new\s+nuint\(', 'new UIntPtr(', 'new nuint('),
        (r'await\s+using\s+var\s+', 'using var ', 'await using var'),
        (r'await\s+using\s+\(', 'using (', 'await using ('),
        (r'Math\.Clamp\s*\(', 'MathPolyfills.Clamp(', 'Math.Clamp'),
        (r'File\.WriteAllTextAsync', 'FilePolyfills.WriteAllTextAsync', 'File.WriteAllTextAsync'),
        (r'File\.ReadAllTextAsync',  'FilePolyfills.ReadAllTextAsync',  'File.ReadAllTextAsync'),
        (r'File\.ReadAllBytesAsync', 'FilePolyfills.ReadAllBytesAsync', 'File.ReadAllBytesAsync'),
        (r'File\.WriteAllBytesAsync','FilePolyfills.WriteAllBytesAsync','File.WriteAllBytesAsync'),
        (r'Enum\.Parse<', 'EnumPolyfills.Parse<', 'Enum.Parse<'),
        (r'Enum\.TryParse<', 'EnumPolyfills.TryParse<', 'Enum.TryParse<'),
        (r'UnixFileMode', 'int /* net48: was UnixFileMode */', 'UnixFileMode'),
    ]

    for p in cs_files:
        content = p.read_text(encoding='utf-8-sig')
        original = content
        labels = []
        for pat, repl, label in patterns:
            if re.search(pat, content):
                content = re.sub(pat, repl, content)
                labels.append(label)
        if content != original:
            # Don't actually write; just report (this is a dry-run validation)
            n += 1
            print(f"  would patch: {p.relative_to(src)} [{', '.join(labels)}]")
    print(f"[rewrite-source] {n} files would be patched")
    return n


def run_targeted_patches(src: Path) -> int:
    """Equivalent of patch-targeted.ps1"""
    print("\n[patch-targeted]")
    n = 0

    # Patch 1: ServiceLib.csproj - comment out UdpTest ref
    csproj = src / "ServiceLib" / "ServiceLib.csproj"
    if csproj.exists():
        c = csproj.read_text(encoding='utf-8-sig')
        if 'ServiceLib.UdpTest' in c and 'NET48 PORT: UdpTest disabled' not in c:
            n += 1
            print(f"  would patch: ServiceLib/ServiceLib.csproj (disable UdpTest ref)")

    # Patch 2: SpeedtestService.cs - stub DoUdpTest
    ss = src / "ServiceLib" / "Services" / "SpeedtestService.cs"
    if ss.exists():
        c = ss.read_text(encoding='utf-8-sig')
        if 'using ServiceLib.UdpTest' in c and 'NET48 PORT: UdpTest disabled' not in c:
            n += 1
            print(f"  would patch: ServiceLib/Services/SpeedtestService.cs (stub DoUdpTest)")

    # Patch 3: DownloaderHelper.cs - SocketsHttpHandler -> HttpClientHandler
    dh = src / "ServiceLib" / "Helper" / "DownloaderHelper.cs"
    if dh.exists():
        c = dh.read_text(encoding='utf-8-sig')
        if 'SocketsHttpHandler' in c and 'NET48 PORT: HttpClientHandler' not in c:
            n += 1
            print(f"  would patch: ServiceLib/Helper/DownloaderHelper.cs (SocketsHttpHandler->HttpClientHandler)")

    # Patch 4: FileUtils.cs - stub TarFile
    fu = src / "ServiceLib" / "Common" / "FileUtils.cs"
    if fu.exists():
        c = fu.read_text(encoding='utf-8-sig')
        if 'using System.Formats.Tar' in c and 'NET48 PORT: System.Formats.Tar not available' not in c:
            n += 1
            print(f"  would patch: ServiceLib/Common/FileUtils.cs (stub TarFile)")

    # Patch 5: GlobalUsings.cs - add System.Net.Http
    gu = src / "ServiceLib" / "GlobalUsings.cs"
    if gu.exists():
        c = gu.read_text(encoding='utf-8-sig')
        if 'global using System.Net.Http' not in c:
            n += 1
            print(f"  would patch: ServiceLib/GlobalUsings.cs (add System.Net.Http)")

    print(f"[patch-targeted] {n} files would be patched")
    return n


if __name__ == '__main__':
    if len(sys.argv) != 2:
        sys.exit('usage: validate-patches.py <src_dir>')
    main(sys.argv[1])
