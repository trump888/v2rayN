# net48 port

Builds [v2rayN](https://github.com/2dust/v2rayN) as a **.NET Framework 4.8**
WPF application, so it runs on Windows 7 SP1 / Windows 10 / Windows 11 and does
not require the .NET 10 desktop runtime.

The v2rayN source itself is **not** modified in this repo. This directory holds a
patch pipeline applied to a pristine upstream checkout, so the port can be
rebased onto new upstream releases without hand-merging.

## Quick start

```bash
# CI resolves the newest upstream release tag and builds it for net48.
# Locally, on Linux/macOS, you can verify the portable half of the port:
./net48-patches/scripts/verify-local.sh
```

`verify-local.sh` stages the upstream source, applies the patches, builds
`ServiceLib` for `net48` using `Microsoft.NETFramework.ReferenceAssemblies`, and
asserts the native SQLite library actually reaches the output. That catches
essentially all port regressions in ~30 s instead of a ~10 min CI round trip.
The WPF project and `AmazTool` need Windows/MSBuild and are compile-gated by
`.github/workflows/build-net48.yml`.

## Layout

| Path | Role |
| --- | --- |
| `scripts/apply-patches.ps1` | Entry point. Copies project/shim files, runs the rewriters, then validates. |
| `scripts/rewrite-source.ps1` | Broad, pattern-based source rewrites for APIs net48 lacks. |
| `scripts/patch-targeted.ps1` | Per-file patches that need judgement (Downloader API skew, `DoUdpTest`, Tar extraction, EventChannel lock). |
| `patches/` | Whole-file replacements (`.csproj`, `Directory.*.props`). |
| `shims/` | C# source copied into the patched tree to supply missing BCL surface. |
| `scripts/verify-local.sh` | Fast local check (Linux/macOS). |
| `scripts/recheck.sh` | Re-apply + rebuild an already-staged tree, for tight iteration. |

## Why this tracks upstream releases cheaply

The expensive part of a net48 port is normally **version skew**: every package
downgrade makes upstream source fail to compile, so each upstream release costs
a round of API archaeology.

Almost all of that is gone here. The port matches upstream's own package
versions, because nearly every package still ships a `net48`-consumable asset:

| Package | Upstream | net48 asset used |
| --- | --- | --- |
| ReactiveUI | 24.3.0 | `lib/net481` |
| ReactiveUI.WPF | 24.2.0 | `lib/net481` |
| MaterialDesignThemes | 5.3.2 | `lib/net462` |
| NLog | 6.2.1 | `lib/net46` |
| TaskScheduler | 2.12.2 | `lib/net48` |
| CliWrap / YamlDotNet / WebDav.Client / QRCoder / IPNetwork2 / sqlite-net-e | upstream | `netstandard2.0` or `net4x` |
| **Downloader** | **5.9.8** | **3.1.2** — the only real pin |

A `net48` project binds a `net481` asset (a later 4.x TFM satisfies an earlier
one), which is what makes matching ReactiveUI possible.

Two consequences worth knowing:

- **`ReactiveUI.Fody` is gone.** ReactiveUI 24 replaced the Fody `[Reactive]`
  weaver with partial-property source generators, so the port uses
  `ReactiveUI.SourceGenerators`, exactly like upstream. Upstream's `[Reactive]`
  partial properties now compile unmodified.
- **`Downloader` stays on 3.1.2.** 3.2.0 dropped `lib/net462` *and*
  `lib/netstandard2.0` and ships `lib/net8.0` only, so it cannot bind on net48.
  A handful of 5.x members are handled in `patch-targeted.ps1`; the retry count
  is *renamed* (`MaxTryAgainOnFailure` → `MaxTryAgainOnFailover`) rather than
  stripped, so retries still work.

## `RuntimeIdentifier` is load-bearing

`Directory.Build.props` sets `RuntimeIdentifier=win-x64`. This is **not**
cosmetic, and removing it produces a build that compiles cleanly and then fails
to start:

- The native SQLite library (`e_sqlite3.dll`) comes from
  `Repobot.SQLite.Unofficial`'s `runtimes/win-x64/native/` assets.
- That package ships **no** `buildTransitive` targets, so nothing copies the
  native for a .NET Framework build on its own. The SDK only copies
  `runtimes/` assets into the output when a RID is set.
- `SqliteHelper`'s constructor opens the database, and `App.OnStartup` calls it
  (`CreateTable<SubItem>()`) *before* `mainWindow.Show()`.
- The resulting `DllNotFoundException` is swallowed by
  `App_DispatcherUnhandledException`, and `ShutdownMode` is
  `OnExplicitShutdown` — so the process stays alive with no window and no error
  dialog.

`AppendRuntimeIdentifierToOutputPath=false` keeps the output at
`bin/<cfg>/net48/`, so CI paths and the zip layout are unchanged.

Both `verify-local.sh` and the CI gate
(`Verify publish output is runnable`) assert the native is present, so this
cannot regress silently again.

## What the shims are for

net48 predates a lot of the BCL surface upstream uses. Rather than rewriting
upstream logic (which rots on every rebase), the port supplies the missing
types and members as additive extensions:

- `Lock` — `System.Threading.Lock` is .NET 9+. The shim must match the BCL shape
  exactly (`public sealed class`, nested `public readonly ref struct Scope`,
  `EnterScope()`) or `lock` statements fail with CS0656.
- `CodeAnalysisNullability` — net48's mscorlib declares the nullability
  attributes but marks them `internal`, so the ReactiveUI source generator's
  `[MemberNotNull]` output failed with CS0122.
- `BclNet6Polyfills` — `PeriodicTimer`, `Parallel.ForEachAsync`,
  `MaxBy`/`MinBy`/`DistinctBy`/`Chunk`, generic `Enum` helpers,
  `ReadOnlySpan<char>` helpers, and a C# 14 `IsEmpty` extension **property**
  (upstream writes `span.IsEmpty` without parentheses, so a method would bind as
  a method group).
- `TarPolyfills` — `System.Formats.Tar` is .NET 7+. Win10 1709+/Win11 ship
  `tar.exe`, so archives are extracted through it, with an actionable error if
  it is absent.

### Two places where a shim would have been a *bug*

Both compiled fine and failed at runtime, which is why they are called out:

- **`EventChannel<T>` gate.** Upstream uses one `Lock` two ways: `lock (_gate)`
  (which becomes `Lock.EnterScope()` on .NET 9+) and `_signal.Synchronize(_gate)`.
  ReactiveUI 24's `Synchronize` takes a plain `object` and monitor-locks it. If
  `_gate` stayed a shimmed `Lock`, the two sides would guard the same field with
  two *different* locks and the mutual exclusion would silently not hold. The
  port therefore declares the gate as `object` so both sides take the same
  monitor. The C# 14 `extension` property shim requires `LangVersion=preview`,
  which `Directory.Build.props` sets.
- **`RxAppBuilder` / MaterialDesign / `RxSchedulers` shims.** Now provided by the
  real packages. An `internal sealed class RxAppBuilder` in namespace
  `ReactiveUI.Builder` shadows the imported one, so the app would build but skip
  ReactiveUI's WPF platform registration. ReactiveUI 24 also no longer ships
  `System.Reactive`, so those shims would not even compile.

## Known behaviour differences from upstream

| Area | Difference |
| --- | --- |
| UDP speed test | Stubbed; reports `-1` (`ServiceLib.UdpTest` needs .NET 5+ `Stream`/`Socket` overloads) |
| Tar archives | Extracted via `tar.exe`, not `System.Formats.Tar` |
| `CertificateChainPolicy` | Ignored; uses the system trust store |
| Download retries | `MaxTryAgainOnFailover`; chunk-size tuning reduced to `MinimumSizeOfChunking` |
| Avalonia desktop frontend | Removed (WPF frontend only) |
| `ServiceLib.Tests` | Not shipped. Upstream's TUnit runner requires .NET 8+ |

## Guarding against silent drift

The dangerous failure mode for a port like this is not "it fails to build" —
that is loud. It is "a regex stopped matching after an upstream refactor", or "a
package stopped shipping a needed runtime asset", which degrade behaviour or
crash at startup while still compiling.

So the pipeline fails loudly instead:

- Every rewriter that recognises a construct it cannot rewrite records it as
  *unhandled*, and the run exits non-zero.
- `apply-patches.ps1` ends with a validation pass that checks the patched tree
  for leftover `SocketsHttpHandler`, `ReactiveUI.Fody`, `System.Formats.Tar`,
  `MaxTryAgainOnFailure`, `CustomHttpMessageHandlerFactory`,
  `MinimumChunkSize`, a non-`net48` `TargetFramework`, a missing
  `ReactiveUI.SourceGenerators` reference, a missing `RuntimeIdentifier`, and an
  unconditional `UdpTest` reference.
- CI additionally asserts the publish output is *runnable*: required managed and
  native assemblies present, no Fody artifacts, and binding redirects actually
  present in `v2rayN.exe.config`.
- Rewrites are line-anchored on purpose. An earlier `[^;]+;` pattern could
  swallow newlines and delete the closing `};` of an object initializer along
  with real code; that class of pattern is banned.

## WPF project: what is and is not verified

`verify-local.sh` cannot build `v2rayN` (WPF) or `AmazTool`, because `UseWPF`
needs a Windows SDK. Those two are compile-gated by CI. Everything else here was
verified locally against upstream `10efd1a5`.

Where a rewrite depended on an exact third-party signature, it was checked
against the shipped assembly rather than assumed. Example: `SimpleViewLocator`
implements `ReactiveUI.IViewLocator`, and the port strips the `new()` constraint
from `ResolveView<T>`. That is only legal if the interface no longer declares
it (C# requires an implementing method's constraints to match exactly, so
getting this wrong is a CS0425 compile error). Reading the generic-parameter
constraints out of `ReactiveUI.Core` 24.3.0 shows `ResolveView<TViewModel>` has
`constraints=[]` with only `ReferenceTypeConstraint` — i.e. `where T : class`,
no `new()` — which is what the patched file declares.

## Rebasing onto a new upstream release

1. Run `./net48-patches/scripts/verify-local.sh`.
2. If validation or the build fails, fix the specific rewriter. Prefer adding a
   shim or a rename over editing upstream-shaped source, so the next rebase is
   cheaper.
3. If a package stops shipping a `net48`-consumable asset, re-probe before
   pinning anything older:

   ```bash
   curl -s "https://api.nuget.org/v3-flatcontainer/<pkg>/index.json"
   # then inspect lib/ + ref/ folders inside the .nupkg for net4x/netstandard2.0
   ```

4. Push. CI resolves the newest upstream release automatically; to try a specific
   tag, dispatch the workflow with `release_version` set.

## When the UI does not come up

`App.xaml.cs` installs a `DispatcherUnhandledException` handler that sets
`e.Handled = true` and only logs, so **startup failures produce no dialog**, and
`ShutdownMode="OnExplicitShutdown"` means the process lingers without a window.
Always check the log first:

```
<exe dir>\guiLogs\<yyyyMMdd>.txt
```

(or `%LOCALAPPDATA%\v2rayN\guiLogs\` when the `v2raynet_AppData` env var is set).