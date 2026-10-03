#!/usr/bin/env bash
# =============================================================================
# recheck.sh  —  fast inner loop while iterating on the net48 port.
# =============================================================================
# Re-applies the patches to an already-staged tree and rebuilds, skipping the
# git-archive re-stage. Use verify-local.sh for the authoritative from-clean run.
#
#   ./recheck.sh            # reuse /tmp/kilo/net48-verify/src
#   ./recheck.sh <dir>      # reuse <dir>
# =============================================================================
set -uo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PATCH_ROOT="$REPO_ROOT/net48-patches"
SCRATCH="${1:-/tmp/kilo/net48-verify/src}"
DOTNET="${DOTNET:-/tmp/kilo/tools/dotnet/dotnet}"

if [ ! -f "$SCRATCH/Directory.Build.props" ]; then
  echo "no staged tree at $SCRATCH - run verify-local.sh first" >&2
  exit 1
fi

pwsh -NoProfile -File "$PATCH_ROOT/scripts/apply-patches.ps1" -SourceDir "$SCRATCH" \
  > "$SCRATCH/../apply.log" 2>&1
if [ $? -ne 0 ]; then
  echo "=== apply-patches FAILED ==="
  tail -30 "$SCRATCH/../apply.log"
  exit 1
fi

cd "$SCRATCH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

"$DOTNET" build ServiceLib/ServiceLib.csproj \
  -c Release -p:TargetFramework=net48 -v minimal -nologo 2>&1 \
  | grep -E 'error|Build succeeded|Build FAILED|Error\(s\)' \
  | sed "s#$SCRATCH/##g; s# \[$SCRATCH.*##g" \
  | sort -u | head -"${MAX_ERRORS:-60}"