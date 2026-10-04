#!/usr/bin/env bash
# =============================================================================
# check-core-assets.sh -- verify the core download URLs the port builds
# =============================================================================
# The Windows 7 core-update support in ServiceLib/Common/Win7Compat.cs works by
# requesting assets under different names than upstream does:
#
#   sing-box   ...-windows-amd64.zip                     (upstream)
#              ...-windows-amd64-legacy-windows-7.zip    (Win7)
#   mihomo     mihomo-windows-amd64-v1-{ver}.zip         (upstream)
#              mihomo-windows-amd64-v1-go120-{ver}.zip   (Win7)
#   Xray       Xray-windows-64.zip -- same name either way, but on Win7 the
#              release is pinned to v1.8.3 (the last Go 1.20 build)
#
# Those names belong to upstream, not to us. If a project renames or drops one,
# the port still compiles and the update page still lists all three cores; the
# failure only surfaces as a 404 at update time, on the machine that can least
# afford it. So assert the names exist against the release metadata.
#
# This reads the asset list from the GitHub API rather than downloading the
# files: the mihomo go120 zip alone is 25 MB, and pulling ~50 MB per run gets
# the release CDN to throttle, which shows up as spurious 404s. The API answers
# the only question being asked -- does an asset with this name exist?
#
# Usage:  ./check-core-assets.sh
# Env:    GITHUB_TOKEN (optional, raises the API rate limit)
# Exit:   0 = every expected asset name is published, 1 = at least one is not
# =============================================================================
set -uo pipefail

API="${GITHUB_API:-https://api.github.com}"
UA="v2rayN-net48-asset-check"

# Prefer the authenticated `gh` CLI when it is available: unauthenticated
# api.github.com allows only 60 requests/hour, which this script exhausts and
# which then masquerades as "upstream renamed the asset".
USE_GH=0
if [ -z "${GITHUB_TOKEN:-}" ] && command -v gh >/dev/null 2>&1 && gh auth status >/dev/null 2>&1; then
    USE_GH=1
elif [ -n "${GITHUB_TOKEN:-}" ]; then
    AUTH="Authorization: Bearer ${GITHUB_TOKEN}"
else
    AUTH=""
    echo "note: unauthenticated (60 requests/hour). Set GITHUB_TOKEN or use 'gh auth login'"
    echo "      to avoid rate-limit false negatives."
fi

pass=0
fail=0
rate_limited=0

api_get() {
    if [ "$USE_GH" = "1" ]; then
        gh api "$1" 2>/dev/null
    else
        # shellcheck disable=SC2086
        curl -s -H "User-Agent: $UA" ${AUTH:+"-H" "$AUTH"} "$1"
    fi
}

# latest non-prerelease tag for owner/repo.
# Uses the github.com web URL, which answers HEAD /releases/latest with a 302
# to /releases/tag/<tag>. api.github.com/repos/../releases/latest would return
# JSON instead, which needs a JSON parser we do not want to depend on here.
latest_tag() {
    curl -sI -H "User-Agent: $UA" "https://github.com/$1/releases/latest" \
        | tr -d '\r' | awk 'tolower($1) == "location:" { print $2 }' \
        | sed 's#.*/tag/##'
}

# does owner/repo@tag publish an asset called exactly $2?
# The API returns minified JSON ("name":"x.zip") but tolerate the pretty-printed
# form too, in case that ever changes.
has_asset() {
    local repo="$1" tag="$2" want="$3" body
    body="$(api_get "$API/repos/$repo/releases/tags/$tag")"
    # A rate-limited response is not evidence of anything. Say so, and do not
    # report it as a missing asset.
    if printf '%s' "$body" | grep -qE '"message":[[:space:]]*"(API rate limit|secondary rate)' ; then
        return 2
    fi
    printf '%s' "$body" \
        | grep -E -q "\"name\":[[:space:]]*\"$(printf '%s' "$want" | sed 's/[.[\*^$()+?{|]/\\&/g')\""
}

report() {
    local repo="$1" tag="$2" asset="$3" label="$4" rc
    has_asset "$repo" "$tag" "$asset"
    rc=$?
    case "$rc" in
        0) printf '  ok    %-24s %s\n' "$label" "$asset"
           pass=$((pass + 1)) ;;
        2) printf '  SKIP  %-24s %s\n' "$label" "$asset"
           printf '        (GitHub API rate limited -- cannot verify)\n'
           rate_limited=$((rate_limited + 1)) ;;
        *) printf '  FAIL  %-24s %s\n' "$label" "$asset"
           printf '        (not published for %s %s -- upstream may have renamed it)\n' "$repo" "$tag"
           fail=$((fail + 1)) ;;
    esac
}

echo "== sing-box =="
SB_TAG="$(latest_tag SagerNet/sing-box)"
if [ -z "$SB_TAG" ]; then
    echo "  FAIL  could not resolve the latest sing-box release tag"
    fail=$((fail + 1))
else
    # sing-box assets embed the version WITHOUT the leading "v":
    #   sing-box-1.14.2-windows-amd64.zip
    # which is what CoreInfoManager's template does via RemovePrefix("v").
    SB_VER="${SB_TAG#v}"
    echo "  latest: $SB_TAG"
    report SagerNet/sing-box "$SB_TAG" "sing-box-${SB_VER}-windows-amd64.zip"               "upstream (Win10+)"
    report SagerNet/sing-box "$SB_TAG" "sing-box-${SB_VER}-windows-amd64-legacy-windows-7.zip" "Win7 compat"
fi

echo "== mihomo =="
MH_TAG="$(latest_tag MetaCubeX/mihomo)"
if [ -z "$MH_TAG" ]; then
    echo "  FAIL  could not resolve the latest mihomo release tag"
    fail=$((fail + 1))
else
    # mihomo is the opposite: its assets embed the tag WITH the leading "v",
    #   mihomo-windows-amd64-v1-v1.19.32.zip
    #   mihomo-windows-amd64-v1-go120-v1.19.32.zip
    # so the "v" must NOT be stripped here. This is why CoreInfoManager's
    # template uses {0} (SemanticVersion.ToString() == raw, "v1.19.32") rather
    # than a de-prefixed version.
    echo "  latest: $MH_TAG"
    report MetaCubeX/mihomo "$MH_TAG" "mihomo-windows-amd64-v1-${MH_TAG}.zip"       "upstream (Win10+)"
    report MetaCubeX/mihomo "$MH_TAG" "mihomo-windows-amd64-v1-go120-${MH_TAG}.zip" "Win7 compat"
fi

echo "== Xray =="
# Pinned, not latest: v1.8.3 is the last release built with Go 1.20, and Go
# 1.21+ requires Windows 10. Keep in sync with Win7Compat.XrayLastWin7Tag.
XR_TAG="${XRAY_WIN7_TAG:-v1.8.3}"
echo "  pinned: $XR_TAG  (last Go 1.20 build; keep in sync with Win7Compat.XrayLastWin7Tag)"
report XTLS/Xray-core "$XR_TAG" "Xray-windows-64.zip" "Win7 pinned release"

echo
echo "core asset check: ${pass} ok, ${fail} failed, ${rate_limited} skipped"
if [ "$rate_limited" -gt 0 ]; then
    echo "INCONCLUSIVE: some checks were skipped because the GitHub API rate limit was hit."
    exit 2
fi
[ "$fail" -eq 0 ] || exit 1
exit 0