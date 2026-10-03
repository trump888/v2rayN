// ============================================================================
// SupportedOSPlatform.cs  --  net48 polyfill for OS-platform attributes
// ============================================================================
// .NET 5+ ships `[SupportedOSPlatform("...")]` and related attributes in
// System.Runtime.Versioning. .NET Framework 4.x does not. Since the v2rayN
// source annotates many methods with these attributes (for the .NET 10
// analyzer to enforce platform guards), we provide no-op stubs here so
// the source compiles unmodified.
//
// These stubs have NO runtime effect on net48 — they exist only to satisfy
// the compiler. On net48 the entire binary runs on Windows anyway, so the
// platform guards are meaningless.
// ============================================================================

namespace System.Runtime.Versioning
{
    [AttributeUsage(AttributeTargets.Assembly
                  | AttributeTargets.Class
                  | AttributeTargets.Constructor
                  | AttributeTargets.Enum
                  | AttributeTargets.Event
                  | AttributeTargets.Field
                  | AttributeTargets.Interface
                  | AttributeTargets.Method
                  | AttributeTargets.Module
                  | AttributeTargets.Property
                  | AttributeTargets.Struct,
        AllowMultiple = true, Inherited = false)]
    internal sealed class SupportedOSPlatformAttribute : Attribute
    {
        public SupportedOSPlatformAttribute(string platformName) { }
    }

    [AttributeUsage(AttributeTargets.Assembly
                  | AttributeTargets.Class
                  | AttributeTargets.Constructor
                  | AttributeTargets.Enum
                  | AttributeTargets.Event
                  | AttributeTargets.Field
                  | AttributeTargets.Interface
                  | AttributeTargets.Method
                  | AttributeTargets.Module
                  | AttributeTargets.Property
                  | AttributeTargets.Struct,
        AllowMultiple = true, Inherited = false)]
    internal sealed class UnsupportedOSPlatformAttribute : Attribute
    {
        public UnsupportedOSPlatformAttribute(string platformName) { }
    }

    [AttributeUsage(AttributeTargets.Assembly
                  | AttributeTargets.Class
                  | AttributeTargets.Constructor
                  | AttributeTargets.Enum
                  | AttributeTargets.Event
                  | AttributeTargets.Field
                  | AttributeTargets.Interface
                  | AttributeTargets.Method
                  | AttributeTargets.Module
                  | AttributeTargets.Property
                  | AttributeTargets.Struct,
        AllowMultiple = true, Inherited = false)]
    internal sealed class ObsoletedOSPlatformAttribute : Attribute
    {
        public ObsoletedOSPlatformAttribute(string platformName) { }
        public ObsoletedOSPlatformAttribute(string platformName, string message) { }
    }

    [AttributeUsage(AttributeTargets.Method
                  | AttributeTargets.Property
                  | AttributeTargets.Field
                  | AttributeTargets.Parameter
                  | AttributeTargets.ReturnValue,
        AllowMultiple = true, Inherited = false)]
    internal sealed class SupportedOSPlatformGuardAttribute : Attribute
    {
        public SupportedOSPlatformGuardAttribute(string platformName) { }
    }

    [AttributeUsage(AttributeTargets.Method
                  | AttributeTargets.Property
                  | AttributeTargets.Field
                  | AttributeTargets.Parameter
                  | AttributeTargets.ReturnValue,
        AllowMultiple = true, Inherited = false)]
    internal sealed class UnsupportedOSPlatformGuardAttribute : Attribute
    {
        public UnsupportedOSPlatformGuardAttribute(string platformName) { }
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>Required by C# 11 `required` keyword on net48.</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }

    // The nullable-flow attributes (NotNullWhen, MaybeNullWhen, NotNull,
    // MaybeNull, MemberNotNull, ...) used to be declared here as `internal`.
    // They now live in CodeAnalysisNullability.cs as `public`, because the
    // ReactiveUI.SourceGenerators output applies them from generated code and
    // could not see an internal type (CS0122).
}

namespace System.Runtime.CompilerServices
{
    /// <summary>Required by C# 11 `required` keyword on net48.</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute { }

    /// <summary>Required by C# 11 `required` keyword on net48.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) { }
    }

    /// <summary>Required by ArgumentNullException.ThrowIfNull(paramName) on net48.
    /// On .NET 6+ this is a real attribute; on net48 we redefine it.</summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute : Attribute
    {
        public CallerArgumentExpressionAttribute(string parameterName) { }
    }
}
