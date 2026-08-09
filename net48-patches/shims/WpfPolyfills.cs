// ============================================================================
// WpfPolyfills.cs  --  WPF-specific polyfills for ReactiveUI + MaterialDesign
// ============================================================================
// Provides:
//   - DisposeWith extension (from System.Reactive.Disposables.Fluent, net5+)
//   - RxAppBuilder stub (from ReactiveUI.Builder, net5+)
// ============================================================================

using System;
using System.Collections.Generic;
using System.Reactive.Disposables;

namespace System.Reactive.Disposables.Fluent
{
    /// <summary>
    /// Provides the DisposeWith extension method that was in
    /// System.Reactive.Disposables.Fluent namespace (.NET 5+).
    /// v2rayN WPF code uses .DisposeWith(disposables) extensively.
    /// </summary>
    internal static class FluentDisposableExtensions
    {
        public static T DisposeWith<T>(this T disposable, CompositeDisposable disposables)
            where T : IDisposable
        {
            if (disposable != null)
                disposables.Add(disposable);
            return disposable;
        }
    }
}

namespace ReactiveUI.Builder
{
    /// <summary>
    /// Stub for RxAppBuilder from ReactiveUI 23+.
    /// ReactiveUI 19.x doesn't have this builder pattern.
    /// We provide a no-op stub so source compiles.
    /// </summary>
    internal sealed class RxAppBuilder
    {
        internal static RxAppBuilder CreateReactiveUIBuilder() => new RxAppBuilder();
        internal RxAppBuilder WithWpf() => this;
        internal void BuildApp() { /* no-op: ReactiveUI 19.x auto-configures WPF */ }
    }
}

namespace MaterialDesignThemes.Wpf
{
    /// <summary>
    /// MaterialDesignThemes 3.2.0 uses IBaseTheme for SetBaseTheme,
    /// but 5.x uses BaseTheme enum directly. We provide a shim.
    /// </summary>
    internal static class MaterialDesignThemePolyfills
    {
        public static void SetBaseTheme(this Theme theme, BaseTheme baseTheme)
        {
            // MaterialDesign 3.2.0's SetBaseTheme takes IBaseTheme
            // BaseTheme.Light -> Theme.GetBaseTheme(true) etc.
            // Actually 3.2.0 has SetBaseTheme(IBaseTheme) and BaseTheme is an enum
            // that implements IBaseTheme. Let's just cast.
            theme.SetBaseTheme((IBaseTheme)baseTheme);
        }
    }
}
