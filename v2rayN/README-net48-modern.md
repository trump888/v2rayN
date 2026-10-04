# v2rayN 5.39 (net48) — maintained

A maintained version of the **lightweight** v2rayN: the 5.39 codebase, targeting
.NET Framework 4.8, kept honest with the verification work done on
[`net48-port-2026.10`](../net48-port-2026.10).

## Why this branch exists

5.39 is genuinely the small one, and it stays that way. Measured from real build
artifacts, app payload only (no cores):

| | 5.39 (this branch) | `net48-port-2026.10` |
| --- | --- | --- |
| app payload | **15.9 MB** | 35.2 MB |
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

## The x86 gRPC native: -9.5 MB

`Grpc.Core` ships its native transport per architecture under
`runtimes/win/<arch>/native/`. With no `RuntimeIdentifier` (this solution is
`AnyCPU`) the SDK copies every one, so the artifact carried both:

```
grpc_csharp_ext.x64.dll   12.10 MB
grpc_csharp_ext.x86.dll    9.52 MB   <- 36% of the output, never loadable in a 64-bit process
```

Removed in `v2rayN/Directory.Build.targets`, which filters the copy lists and also
deletes post-build. The delete is deliberate: which item group an RID-less
`runtimes/**` asset lands in varies by SDK version, and a wrong item name is a
silent no-op rather than an error. (MSBuild did confirm the filter targets the
right group — `RuntimeCopyLocalItems` — when a metadata-syntax mistake in it
surfaced as MSB4096.)

**Trade-off:** this makes the artifact effectively **x64-only**. A 32-bit Windows
process would no longer find the native, and the traffic-statistics feature
(which speaks gRPC over it — `Handler/StatisticsHandler`, `Handler/V2rayConfigHandler`)
would fail there. 32-bit Windows is a small minority and the sibling branch is
already x64-only, so the two behave consistently. Deleting
`v2rayN/Directory.Build.targets` reverts it.

## Broken core repositories — needs a product decision

The liveness check reports these on every build. They are **not** build fixes:
which cores this app should offer is a judgement call.

| Repository | Status |
| --- | --- |
| `Dreamacro/clash` | **404, deleted.** Successor is `MetaCubeX/mihomo`, already present |
| `zzzgydi/mieru` | **404, deleted** |
| `SagerNet/v2ray-core` | last push **2022-07-30**. Maintained fork is `v2fly/v2ray-core`, already present |

So of the three, two have maintained successors already wired up in `Global.cs`.
The likely resolution is to drop the three dead entries rather than repoint them,
but that removes functionality from the UI and should be an explicit decision.

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