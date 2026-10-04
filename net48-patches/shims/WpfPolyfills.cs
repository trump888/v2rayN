// ============================================================================
// WpfPolyfills.cs  --  WPF-specific polyfills for the net48 port
// ============================================================================
// History: this file used to shim three things that ReactiveUI 18.x /
// MaterialDesign 3.2.0 did not have. With the port now on ReactiveUI 24.x and
// MaterialDesignThemes 5.3.2 (the same versions upstream uses) all three are
// provided by the real packages:
//
//   * ReactiveUI.Builder.RxAppBuilder      -> ReactiveUI.WPF 24.2.0
//   * ITheme.SetBaseTheme(BaseTheme)       -> MaterialDesignThemes 5.3.2
//   * IDisposable.DisposeWith(set)         -> ReactiveUI 24.x
//
// Keeping local copies would be actively harmful: an `internal sealed class
// RxAppBuilder` in namespace ReactiveUI.Builder shadows the imported one, so
// the app would build but silently skip ReactiveUI's WPF platform registration
// (no scheduler, no activation mode) - a runtime failure, not a compile error.
//
// What remains is the DisposeWith shim. Upstream call sites are rewritten
// .DisposeWith( -> .__DisposeWith( by rewrite-source.ps1, because
// DisposableMixins.DisposeWith constrains its receiver to IDisposable and
// several upstream call sites pass binding expressions that do not implement it.
// The receiver here stays unconstrained for that reason.
//
// THE PARAMETER TYPE IS LOAD-BEARING -- read before changing it to `object`.
//
// ReactiveUI.WpfViewForMixins declares two WhenActivated overloads:
//
//   WhenActivated<TView>(TView, Action<Action<IDisposable>>)
//   WhenActivated<TView>(TView, Action<MultipleDisposable>>)
//
// Upstream bodies use `disposables` only as `x.DisposeWith(disposables)`, so the
// lambda parameter type has to be pinned by whatever DisposeWith accepts.
// ReactiveUI's own DisposeWith takes a MultipleDisposable, which implements
// ICollection<IDisposable>, and Action<IDisposable> implements neither -- so on
// net10 exactly one overload is applicable and the call is unambiguous.
//
// Typing this parameter as `object` (as an earlier revision did) makes *both*
// overloads applicable, because every lambda body here is also valid when
// `disposables` is an object. That produced CS0121 "the call is ambiguous" in
// all 20 WhenActivated blocks in the WPF project. Typing it as
// ICollection<IDisposable> restores upstream's resolution exactly: the
// Action<IDisposable> overload stops being applicable and
// Action<MultipleDisposable> wins, which is the one upstream binds.
//
// ICollection<IDisposable> is preferred over naming MultipleDisposable so the
// shim survives ReactiveUI renaming that type again, and so Add is a direct
// interface call rather than a cached reflective MethodInfo.Invoke - that
// removes the ConcurrentDictionary and its per-type cache entries entirely.
// ============================================================================

using System;
using System.Collections.Generic;

namespace v2rayN.Common
{
    internal static class DisposeWithExtensions
    {
        /// <summary>
        /// Adds <paramref name="disposable"/> to <paramref name="disposables"/>
        /// and returns it unchanged, mirroring ReactiveUI's fluent DisposeWith.
        /// </summary>
        public static T __DisposeWith<T>(this T disposable, ICollection<IDisposable> disposables)
        {
            // The receiver stays unconstrained because upstream passes binding
            // expressions that are not IDisposable. Everything ReactiveUI hands
            // back from Bind/BindCommand/OneWayBind/Subscribe is, so in practice
            // this always registers; if it ever does not, fail loudly rather
            // than silently leaking, because a forgotten Dispose keeps event
            // handlers bound to a closed ViewModel.
            if (disposable is not IDisposable d)
            {
                throw new NotSupportedException(
                    $"net48 port: '{typeof(T).FullName}' is not IDisposable, so " +
                    "DisposeWith cannot register it.");
            }

            disposables.Add(d);
            return disposable;
        }
    }
}