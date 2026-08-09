// ============================================================================
// WpfPolyfills.cs  --  WPF-specific polyfills for ReactiveUI + MaterialDesign
// ============================================================================
// Provides:
//   - RxAppBuilder stub (from ReactiveUI.Builder, net5+)
//   - MaterialDesign BaseTheme -> IBaseTheme conversion
//
// NOTE: DisposeWith is NOT here — net48's System.Reactive.Disposables.DisposableMixins
// already provides it. Adding a duplicate causes CS0121 ambiguity.
// The source code's `global using System.Reactive.Disposables.Fluent;` is
// harmless (empty namespace), and DisposeWith resolves to DisposableMixins.
// ============================================================================

using System;
using System.Collections.Generic;

namespace ReactiveUI.Builder
{
    /// <summary>
    /// Stub for RxAppBuilder from ReactiveUI 23+.
    /// ReactiveUI 19.x doesn't have this builder pattern.
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
    /// MaterialDesignThemes 3.2.0's Theme.SetBaseTheme takes IBaseTheme,
    /// but 5.x uses BaseTheme enum. BaseTheme implements IBaseTheme on 5.x
    /// but not on 3.2.0. We provide conversion via Theme.Light/BaseTheme.Inherit.
    /// </summary>
    internal static class MaterialDesignThemePolyfills
    {
        public static void SetBaseTheme(this ITheme theme, BaseTheme baseTheme)
        {
            // MaterialDesign 3.2.0: Theme.SetBaseTheme(IBaseTheme)
            // BaseTheme is an enum; convert to IBaseTheme via Theme.GetBaseTheme()
            if (baseTheme == BaseTheme.Dark)
                theme.SetBaseTheme(Theme.Dark);
            else if (baseTheme == BaseTheme.Light)
                theme.SetBaseTheme(Theme.Light);
            else
                theme.SetBaseTheme(Theme.Inherit);
        }
    }
}
