# v2rayN 5.39 (net48) — maintained

A maintained version of the **lightweight** v2rayN: the 5.39 codebase, targeting
.NET Framework 4.8, kept honest with the verification work done on
[`net48-port-2026.10`](../net48-port-2026.10).

## Why this branch exists

5.39 is genuinely the small one, and it stays that way. Measured from real build
artifacts, app payload only (no cores):

| | 5.39 (this branch) | `net48-port-2026.10` |
| --- | --- | --- |
| app payload | **~25 MB** | 35.2 MB |
| projects | 1 WPF + 1 WinForms | WPF + ServiceLib + AmazTool + UdpTest |
| JSON | Newtonsoft.Json 13.0.1 | System.Text.Json 10 + 4 polyfill assemblies |
| logging | log4net 2.0.15 | NLog 6.2.1 |
| QR | ZXing.Net (System.Drawing) | ZXing.Net.Bindings.SkiaSharp + an 11 MB native |
| MVVM | hand-rolled | ReactiveUI 24 + source generators |
| cores shipped in CI artifact | none | xray + sing-box (154 MB) |

The difference is mostly *absence*: no ReactiveUI source generators, no SkiaSharp
native, no System.Text.Json polyfill chain, and the CI artifact contains no cores
at all — it is the app only, so a user chooses what to download.

## What was carried over from `net48-port-2026.10`

The transferable asset from that branch was never the source edits — it is the
**discipline** around them. This branch had a CI that was restore, build, upload.
Nothing checked that the output could actually run, which is a large part of why
it sat unbuilt for eight months.

Now carried over:

- **Publish verification.** Required managed and native files present; every
  strong-named assembly reference either matches the shipped version or has a
  binding redirect; a size budget. Building is not evidence that the output runs.
- **Core repository liveness.** 5.39 names its core repositories as constants
  and predates per-core update management, so a dead URL only appears as a 404
  when a user tries to update. Now surfaced on every build.
- **Nightly schedule**, because this branch tracks no upstream source tree and
  nothing else would notice a dependency or core release moving.
- Two hard-won details in the bind check: MSBuild emits `<dependentAssembly>`
  rather than `<assembly>`, and only *strong-named* assemblies need redirects
  (simple-name assemblies bind by name alone, so flagging them gives false
  failures).

### Deliberately **not** carried over

- **The overlay architecture.** `net48-port-2026.10` is a patch overlay on
  current upstream, so tracking upstream is free. 5.39 is a source fork with a
  different stack; converting it to an overlay means adopting 7.x's structure,
  which is precisely the part that costs the 19 MB. That would defeat the branch.
- **The ReactiveUI fixes** (`IsExternalInit` for source-generator output, the
  `WhenActivated` overload ambiguity, the `IViewLocator` rewrite). 5.39 does not
  use ReactiveUI, so none of them apply.
- **The `supportedOS` manifest fix.** 5.39's only `Environment.OSVersion` use is
  `Major >= 6`, which a hardcoded 6.2 does not break. The bug that fix addressed
  does not exist here.
- **SkiaSharp trimming.** There is no SkiaSharp.

## Known advisories in the dependency set

CI reports **NU1902: log4net < 3.3.0**, GHSA-4f7c-pmjv-c25w (medium): silent
log event loss in `XmlLayout` / `XmlLayoutSchemaLog4J` caused by unescaped XML 1.0
forbidden characters.

**Not applicable to this app**, and deliberately left visible rather than
suppressed. `Tool/Logging.cs` configures a `PatternLayout`
(`%date [%thread] %-5level %logger - %message%newline`) and never instantiates
`XmlLayout` or `XmlLayoutSchemaLog4J`, which are the only affected code paths.

This does **not** resolve the advisory, though, and it is worth being blunt about
that: the entire log4net 2.x line is affected and the fix is 3.3.0+. Moving to
log4net 3.x is a major version with breaking API changes (reworked
`LoggingEvent` and target setup) and needs runtime testing this branch's CI cannot
do — it compiles and asserts, but it cannot exercise a live log4net pipeline.

An `InternalsVisibleTo`-style suppression was tried and removed: `NuGetAuditSuppress`
is an SDK item that `nuget restore` does not understand (MSB4066), and
suppressing a security finding without a mechanism that is honoured is worse than
showing it.

## x86 and x64 are both shipped

`Grpc.Core` ships its native transport per architecture under
`runtimes/win/<arch>/native/`. The solution is `AnyCPU`, so both are needed and
both are present:

```
grpc_csharp_ext.x64.dll   12.10 MB
grpc_csharp_ext.x86.dll    9.52 MB
```

An earlier revision of this branch deleted the x86 copy to save 9.5 MB (36% of the
output), on the reasoning that `AnyCPU` produces a 64-bit process on any 64-bit
Windows. **That was wrong** — `AnyCPU` also has to work on 32-bit Windows, and
without the x86 native the traffic-statistics feature (`Handler/StatisticsHandler`
and friends, which speak gRPC over it) fails there. It was restored.

The file *is* never loaded by a 64-bit process, which is what makes it look
removable, so the verification step now lists **both** natives as required. That
turns "we ship x86" from an accident into something a build failure would catch.
The 22 MB size budget went back to 27 MB for the same reason.

If x86 support is ever dropped deliberately, delete both natives from
`$required` and lower the budget in the same commit.

## Protocol and core coverage has NOT kept up

Measured by comparing `EConfigType` and `ECoreType` against `net48-port-2026.10`.

**Protocols (`EConfigType`).** 5.39 has: VMess, Custom, Shadowsocks, Socks, VLESS,
Trojan, Hysteria2, TUIC, WireGuard, HTTP, Mieru. 7.x additionally has:

| Missing in 5.39 | Notes |
| --- | --- |
| `Anytls` | AnyTLS protocol; sing-box/mihomo support it |
| `MASQUE` | Cloudflare's QUIC-based proxy |
| `Naive` | 5.39 ships `naiveproxy` as a core but has no `Naive` config type, so it cannot be configured through the UI as the newer version can |
| `Outbound` | sing-box outbound-style profiles |
| `PolicyGroup`, `ProxyChain` | 5.x-era profile grouping/chain UI |

And 5.39 has one the newer version dropped: **`Mieru`**.

**Cores.** 5.39 configures: v2fly, v2fly_v5, Xray, SagerNet(v2ray-core), clash,
clashMeta, hysteria, hysteria2, naiveproxy, tuic, sing_box, mieru. 7.x adds four
that 5.39 cannot configure at all:

| Missing in 5.39 | Notes |
| --- | --- |
| `juicity` | |
| `brook` | |
| `overtls` | |
| `shadowquic` | |

5.39 carries `clash` and `SagerNet/v2ray-core`, both of which 7.x dropped because
they are dead (see below). So the honest summary is: **the mainstream protocols
are all there, the long tail is not.**

Closing this gap means hand-porting each protocol's config generation *and* its
share-link parser per core, plus the UI to expose it. It is not a merge; the 5.x
UI has no equivalent of the newer profile editor. Treated as its own piece of
work, not a drive-by.

## Broken cores — found, and mostly fixed

The liveness check reports these on every build. The first survey found more
than it first looked, because a core can be broken in ways a green repository
says nothing about.

| Core | Problem | Resolution |
| --- | --- | --- |
| `Dreamacro/clash` | repository deleted (404) | removed from the UI |
| `zzzgydi/mieru` | repository deleted (404) | removed from the UI |
| `SagerNet/v2ray-core` | last push 2022-07-30; `v2fly/v2ray-core` already offered | removed from the UI |
| **`mihomo`** | **was offered in the UI but had no `CoreInfo` registered at all** — selecting it could not resolve a core | now registered, with current asset and exe names |
| **`clash_meta`** | downloaded *successfully* (GitHub redirects `Clash.Meta` → `mihomo`) but its `Clash.Meta-*` exe names no longer match the `mihomo-windows-amd64.exe` the repo publishes, so the binary was never found | superseded by `mihomo`; removed from the UI |

That last two are the interesting ones: both compile, both run, and both only
fail when a user clicks. A repository-liveness check cannot see either.

`ECoreType` values are **not** renumbered — they are persisted in existing server
configurations, so renumbering would silently repoint every saved profile. The
dead enum values and their `CoreInfo` registrations are kept, so an exe already
downloaded under one of those names keeps working for a profile that still
references it. Only the UI lists changed.

`Global.coreTypes` is now the single source of truth for what the UI offers.
`AddServer2Form` used to build a second list from
`Enum.GetValues(typeof(ECoreType))`, which is why the two dropdowns disagreed and
why `mihomo` could appear in one and not the other.

A new CI step, `Verify offered cores are registered`, asserts every offered core
has a `CoreInfo` and that mihomo's exe name matches what the current release
actually publishes.

## Honest note on "following the new version"

This branch **cannot** track upstream the way `net48-port-2026.10` does, and no
amount of work here changes that. 5.39 is a snapshot of a 2023 tree; 7.x
restructured into `ServiceLib`, swapped JSON libraries, adopted ReactiveUI with
source generators, and adopted System.Text.Json. Forward-porting means manual
per-commit work, forever.

What this branch does instead:

- **Gates** that fail when something breaks, rather than a source tree that drifts.
- **A nightly run**, so dependency and core drift is noticed.
- **The dead-repository check**, which is the most common way a stale fork rots —
  already found three.

Forward-porting a specific upstream fix is still manual, but it is now a change
made against a green, verified build rather than an unknown one.

## Verifying

```bash
gh workflow run build.yml --ref net48-5.39-modern
```

The `Verify publish output is runnable` step is the one that matters. It has
already earned its place on this branch: it caught the output path being wrong
(`bin/Release` vs `bin/Release/net48`) on a build that had actually succeeded.