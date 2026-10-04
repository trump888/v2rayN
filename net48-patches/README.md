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
  `EnterScope()`) or `lock` statements fail with CS0656. Only `ServiceLib` gets
  this copy: no `v2rayN` source references `Lock`, and ServiceLib already
  exposes a public one, so a local copy only warned CS0436.
- `IsExternalInit` — needed by `v2rayN` even though not one of its own sources
  uses `init` or records. `ReactiveUI.SourceGenerators` emits the `[Reactive]`
  partial properties as `init` accessors, and `init` requires the predefined
  `IsExternalInit` type in *the compiling assembly*. ServiceLib's copy is
  `internal`, so it is invisible to `v2rayN` and the build fails CS0518.
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

### The `DisposeWith` shim's parameter type is load-bearing

`WpfPolyfills.cs` declares the `DisposeWith` replacement as
`__DisposeWith<T>(this T disposable, ICollection<IDisposable> disposables)`. The
`ICollection<IDisposable>` is not cosmetic — typing it as `object` breaks the
build in all 20 `WhenActivated` blocks.

`ReactiveUI.WpfViewForMixins` declares **two** overloads:

```csharp
WhenActivated<TView>(TView, Action<Action<IDisposable>>)
WhenActivated<TView>(TView, Action<MultipleDisposable>)
```

Upstream bodies only ever use the parameter as `x.DisposeWith(disposables)`, so
the lambda's parameter type is pinned entirely by whatever `DisposeWith`
accepts. ReactiveUI's own `DisposeWith` takes a `MultipleDisposable`, which
implements `ICollection<IDisposable>`; `Action<IDisposable>` implements neither,
so exactly one overload is applicable and upstream compiles.

Give the shim an `object` parameter and every one of those bodies is *also*
valid for `object`, so both overloads become applicable and the compiler reports
CS0121. Keeping `ICollection<IDisposable>` restores upstream's resolution
exactly, and it makes `Add` a direct interface call — the earlier reflective
`FindAdder` plus its `ConcurrentDictionary` cache are gone.

Verified against the shipped assemblies rather than assumed: `ReactiveUI.Wpf`
24.2.0 `net481` really does carry both overloads, and `MultipleDisposable`
(`ReactiveUI.Disposables` 8.2.0) really does implement `ICollection<IDisposable>`.

### Rewrites that outlived their reason

`SimpleViewLocator.cs` used to be rewritten for a ReactiveUI **19.x**
`IViewLocator`. That rule is gone, because upstream now satisfies
ReactiveUI 24.3.0's interface on its own. The instructive part is how it failed:
its signature half stopped matching once upstream dropped the `= null` default
and the `new()` constraint, so it silently did nothing, while the body half kept
rewriting `factory() as IViewFor<TViewModel>` to `factory() as IViewFor` — which
is what produced CS0266. A partially-applied rewrite is worse than none, so
delete the rule when its premise disappears rather than leaving it to fire on
whatever still matches.

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
  native assemblies present, no Fody artifacts, and every assembly reference in
  the output either matching the shipped version or covered by a binding
  redirect (strong-named assemblies only — see above).
- The port stamps the real upstream version into `Directory.Build.props` at
  apply time, so a build of a new upstream release reports that release's
  version instead of a stale literal.
- Rewrites are line-anchored on purpose. An earlier `[^;]+;` pattern could
  swallow newlines and delete the closing `};` of an object initializer along
  with real code; that class of pattern is banned.

## WPF project: what is and is not verified

`verify-local.sh` cannot build `v2rayN` (WPF) or `AmazTool`, because `UseWPF`
needs a Windows SDK. Those two are compile-gated by
`.github/workflows/build-net48.yml`.

Current state, both paths green with **0 warnings, 0 errors**:

| Built from | Result |
| --- | --- |
| upstream `master` (`3187eeef`) | success — 11 required files, 48 managed assemblies bind |
| latest upstream release `7.25.4` | success — version stamped `7.25.4-net48` |

The published artifact is 76 files including `e_sqlite3.dll`,
`v2rayN.exe.config` and `AmazTool.exe`.

This matters because the WPF project is where every non-obvious breakage lived.
`verify-local.sh` compiles `ServiceLib` only, so CS0518, CS0121 and CS0266 were
all invisible to it — they needed the Windows SDK. Do not treat a green
`verify-local.sh` as evidence the port builds.

Where a rewrite depended on an exact third-party signature, it was checked
against the shipped assembly rather than assumed. Example: `SimpleViewLocator`
implements `ReactiveUI.IViewLocator`, and the port used to strip the `new()`
constraint from `ResolveView<T>`. That is only legal if the interface no longer
declares it (C# requires an implementing method's constraints to match exactly,
so getting this wrong is a CS0425 compile error). Reading the generic-parameter
constraints out of `ReactiveUI.Core` 24.3.0 showed `ResolveView<TViewModel>` had
`constraints=[]` with only `ReferenceTypeConstraint` — i.e. `where T : class`,
no `new()` — which is exactly what upstream already declares. That is why the
rule was deleted rather than fixed.

## Binding redirects on .NET Framework

`AutoGenerateBindingRedirects` (on by default for SDK-style .NET Framework
projects) emits the four redirects the app actually needs, verified in the
built `v2rayN.exe.config`:

| Assembly | Redirect |
| --- | --- |
| `System.Memory` | `4.0.5.0` |
| `System.Buffers` | `4.0.5.0` |
| `System.Threading.Tasks.Extensions` | `4.2.4.0` |
| `Microsoft.Bcl.AsyncInterfaces` | `10.0.0.12` |

What it does **not** need is the `ReactiveUI.Primitives` /
`ReactiveUI.Primitives.Core` / `ReactiveUI.Disposables` `7.0.0.0` → `8.0.0.0`
skew that `ReactiveUI.WPF` 24.2.0 and `Splat` 21.0.0 carry. Those assemblies
ship with an **empty public key token**, and the .NET Framework loader binds
simple-name assemblies by name alone, ignoring the version. Only *strong-named*
mismatches need a redirect. (Upstream has the same version skew; it is invisible
on .NET 10, where the runtime resolves by name and rolls forward regardless.)

The CI gate encodes exactly that distinction rather than a list of names: for
every assembly reference in the output that resolves to a file we also ship,
the referenced version must match the shipped version **or** `v2rayN.exe.config
must redirect it there — and a version mismatch only counts if the shipped
assembly is strong-named. Native DLLs such as `e_sqlite3.dll` have no PE
metadata and are skipped.

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