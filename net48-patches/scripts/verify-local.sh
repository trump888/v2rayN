#!/usr/bin/env bash
# =============================================================================
# verify-local.sh
# =============================================================================
# Local feedback loop for the net48 port.
#
#   1. Stages the committed v2rayN/ source into a scratch directory.
#   2. Runs apply-patches.ps1 exactly like CI does.
#   3. Builds the projects that CAN build off-Windows (ServiceLib) for net48
#      using Microsoft.NETFramework.ReferenceAssemblies.
#   4. Asserts the native SQLite library reaches the output. Without a RID the
#      SDK never copies Repobot.SQLite.Unofficial's runtimes/win-x64/native
#      assets, and the app then dies at startup with DllNotFoundException -
#      silently, because dispatcher exceptions are swallowed and ShutdownMode
#      is OnExplicitShutdown. This is the one runtime failure that also
#      compiles cleanly, so it is worth asserting explicitly.
#
# The WPF project (v2rayN) and AmazTool need Windows/MSBuild, so they are only
# compile-verified in .github/workflows/build-net48.yml.
#
# Usage:
#   ./verify-local.sh [scratch_dir]
# =============================================================================
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PATCH_ROOT="$REPO_ROOT/net48-patches"
SCRATCH="${1:-/tmp/kilo/net48-verify/src}"
DOTNET="${DOTNET:-/tmp/kilo/tools/dotnet/dotnet}"

if [ ! -x "$DOTNET" ]; then
  echo "dotnet not found at $DOTNET" >&2
  echo "Install with:" >&2
  echo "  curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0 --install-dir /tmp/kilo/tools/dotnet" >&2
  exit 1
fi

echo "=== [1/4] Stage committed v2rayN source -> $SCRATCH ==="
rm -rf "$SCRATCH"
mkdir -p "$SCRATCH"
cd "$REPO_ROOT"
git archive HEAD v2rayN | tar -x -C "$SCRATCH" --strip-components=1
# Submodule content is not part of `git archive`; copy it in explicitly.
if [ -d "$REPO_ROOT/v2rayN/GlobalHotKeys/src" ]; then
  mkdir -p "$SCRATCH/GlobalHotKeys"
  cp -a "$REPO_ROOT/v2rayN/GlobalHotKeys/." "$SCRATCH/GlobalHotKeys/"
fi

echo "=== [2/4] Apply net48 patches ==="
pwsh -NoProfile -File "$PATCH_ROOT/scripts/apply-patches.ps1" -SourceDir "$SCRATCH"

echo "=== [3/4] Build ServiceLib for net48 ==="
cd "$SCRATCH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

# Do NOT set BuildNet48=true: that flag is what *re-enables* the
# ServiceLib.UdpTest ProjectReference. UdpTest targets .NET 5+ Stream/Socket
# overloads and is intentionally excluded from the net48 port.
"$DOTNET" build ServiceLib/ServiceLib.csproj \
  -c Release \
  -p:TargetFramework=net48 \
  -v minimal \
  -nologo

echo "=== [4/4] Assert native SQLite reaches the output ==="
OUT="$SCRATCH/ServiceLib/bin/Release/net48"
missing=""
for f in ServiceLib.dll SQLitePCLRaw.core.dll SQLitePCLRaw.provider.e_sqlite3.dll; do
  [ -f "$OUT/$f" ] || missing="$missing $f"
done
if [ -n "$missing" ]; then
  echo "  FAIL: publish output is missing:$missing" >&2
  ls -1 "$OUT" >&2 || true
  exit 1
fi
echo "  managed SQLite provider present: OK"

# The native is delivered only when RuntimeIdentifier is set. For the library
# alone it lands under the RID folder; Directory.Build.props flattens the output
# for the WPF app. Check both shapes so either layout is accepted.
if [ -f "$OUT/e_sqlite3.dll" ] || [ -f "$OUT/win-x64/e_sqlite3.dll" ]; then
  echo "  native e_sqlite3.dll present: OK"
else
  echo "  FAIL: native e_sqlite3.dll was NOT copied to the output." >&2
  echo "        Without it SqliteHelper's constructor throws DllNotFoundException" >&2
  echo "        during App.OnStartup and the app shows no window." >&2
  echo "        Fix: keep <RuntimeIdentifier>win-x64</RuntimeIdentifier> in" >&2
  echo "        net48-patches/patches/Directory.Build.props" >&2
  exit 1
fi

echo ""
echo "=== verify-local: PASS ==="
echo "ServiceLib builds clean for net48 against $(cd "$REPO_ROOT" && git rev-parse --short HEAD)."
echo "The WPF project (v2rayN) and AmazTool require Windows/MSBuild and are"
echo "compile-verified by .github/workflows/build-net48.yml."