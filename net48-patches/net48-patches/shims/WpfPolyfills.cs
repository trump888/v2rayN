// ============================================================================
// WpfPolyfills.cs  --  WPF-specific polyfills for ReactiveUI + MaterialDesign
// ============================================================================
// Provides:
//   - DisposeWith<T>(this T, CompositeDisposable) — NO IDisposable constraint.
//     Works on IReactiveBinding (which doesn't implement IDisposable on the
//     interface due to covariance, but the concrete class does at runtime).
//   - RxAppBuilder stub (from ReactiveUI.Builder, net5+)
//   - MaterialDesign BaseTheme -> IBaseTheme conversion
//
// HOW TO AVOID AMBIGUITY:
//   net48's DisposableMixins.DisposeWith<T>(where T : IDisposable) is in
//   namespace System.Reactive.Disposables. Our DisposeWith is in
//   System.Reactive.Disposables.Fluent. If BOTH namespaces are imported,
//   IDisposable types see both → CS0121 ambiguity.
//
//   Solution: in WPF GlobalUsings.cs, replace:
//     global using System.Reactive.Disposables;
//   with:
//     global using CompositeDisposable = System.Reactive.Disposables.CompositeDisposable;
//
//   This makes CompositeDisposable visible (needed everywhere) but does NOT
//   import DisposableMixins (no more ambiguity). Our DisposeWith in the
//   Fluent namespace is the ONLY DisposeWith visible.
// ============================================================================

using System;
using System.Collections.Generic;

namespace System.Reactive.Disposables.Fluent
{
    internal static class FluentDisposableExtensions
    {
        /// <summary>
        /// Adds disposable to the CompositeDisposable for cleanup.
        /// Accepts ANY type T (no IDisposable constraint) because
        /// IReactiveBinding's interface doesn't declare IDisposable
        /// (covariance), but the concrete class implements it at runtime.
        /// </summary>
        public static T DisposeWith<T>(this T disposable, CompositeDisposable disposables)
        {
            if (disposable is IDisposable d)
                disposables.Add(d);
            return disposable;
        }
    }
}

namespace ReactiveUI.Builder
{
    internal sealed class RxAppBuilder
    {
        internal static RxAppBuilder CreateReactiveUIBuilder() => new RxAppBuilder();
        internal RxAppBuilder WithWpf() => this;
        internal void BuildApp() { /* no-op: ReactiveUI 19.x auto-configures WPF */ }
    }
}

namespace MaterialDesignThemes.Wpf
{
    internal static class MaterialDesignThemePolyfills
    {
        public static void SetBaseTheme(this ITheme theme, BaseTheme baseTheme)
        {
            if (baseTheme == BaseTheme.Dark)
                theme.SetBaseTheme(Theme.Dark);
            else
                theme.SetBaseTheme(Theme.Light);
        }
    }
}
