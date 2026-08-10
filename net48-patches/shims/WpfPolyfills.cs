// ============================================================================
// WpfPolyfills.cs  --  WPF-specific polyfills for ReactiveUI + MaterialDesign
// ============================================================================
// We use __DisposeWith (double underscore) to AVOID any ambiguity with
// net48's DisposableMixins.DisposeWith. Source code is rewritten:
//   .DisposeWith(disposables)  ->  .__DisposeWith(disposables)
//
// This works for BOTH Action<IDisposable> and CompositeDisposable parameters,
// and accepts ANY type T (no IDisposable constraint) because IReactiveBinding
// doesn't implement IDisposable on the interface (covariance limitation).
// ============================================================================

using System;
using System.Reactive.Disposables;

namespace v2rayN.Common
{
    internal static class DisposeWithExtensions
    {
        public static T __DisposeWith<T>(this T disposable, Action<IDisposable> dispose)
        {
            if (disposable is IDisposable d) dispose(d);
            return disposable;
        }

        public static T __DisposeWith<T>(this T disposable, CompositeDisposable disposables)
        {
            if (disposable is IDisposable d) disposables.Add(d);
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
        internal void BuildApp() { }
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
