// =============================================================================
// CodeAnalysisNullability.cs  —  NET48 PORT shim
// =============================================================================
// .NET Framework 4.8's mscorlib declares the nullability annotation attributes
// (AllowNull, NotNull, MaybeNullWhen, MemberNotNull, ...) but marks them
// *internal*, because C# 8 nullable reference types did not exist yet. They are
// therefore invisible to user code, and any code that *applies* one — notably
// the output of ReactiveUI.SourceGenerators, which stamps
// [MemberNotNull(nameof(...))] onto every generated [Reactive] partial property
// — fails to compile with CS0122 "inaccessible due to its protection level".
//
// Re-declaring them as public in this file resolves the lookup to this
// assembly's public copy (verified: no CS0433 ambiguity is produced, because
// the internal mscorlib copies are not candidates for source-level lookup).
//
// These are compile-time only: the runtime ignores them, so the shims are inert
// at execution time and add no behaviour or measurable startup cost.
// =============================================================================

#if !NET5_0_OR_GREATER

namespace System.Diagnostics.CodeAnalysis;

/// <summary>The specified field must be non-null when the method returns.</summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class AllowNullAttribute : Attribute;

/// <summary>The specified field must be null when the method returns.</summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false)]
public sealed class DisallowNullAttribute : Attribute;

/// <summary>The output may be null even if the corresponding type is non-nullable.</summary>
[AttributeUsage(
    AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property |
    AttributeTargets.ReturnValue,
    Inherited = false)]
public sealed class MaybeNullAttribute : Attribute;

/// <summary>The output is non-null even if the corresponding type is nullable.</summary>
[AttributeUsage(
    AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property |
    AttributeTargets.ReturnValue,
    Inherited = false)]
public sealed class NotNullAttribute : Attribute;

/// <summary>The output is null when the method returns the specified value.</summary>
[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue,
    Inherited = false)]
public sealed class MaybeNullWhenAttribute : Attribute
{
    public MaybeNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

    public bool ReturnValue { get; }
}

/// <summary>The output is non-null when the method returns the specified value.</summary>
[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue,
    Inherited = false)]
public sealed class NotNullWhenAttribute : Attribute
{
    public NotNullWhenAttribute(bool returnValue) => ReturnValue = returnValue;

    public bool ReturnValue { get; }
}

/// <summary>The output is non-null if the named parameter is non-null.</summary>
[AttributeUsage(
    AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue,
    Inherited = false)]
public sealed class NotNullIfNotNullAttribute : Attribute
{
    public NotNullIfNotNullAttribute(string parameterName) => ParameterName = parameterName;

    public string ParameterName { get; }
}

/// <summary>Method never returns normally.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class DoesNotReturnAttribute : Attribute;

/// <summary>Method never returns normally if the specified parameter has the given value.</summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class DoesNotReturnIfAttribute : Attribute
{
    public DoesNotReturnIfAttribute(bool parameterValue) => ParameterValue = parameterValue;

    public bool ParameterValue { get; }
}

/// <summary>The listed members must be non-null when the method returns.</summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Property,
    Inherited = false,
    AllowMultiple = true)]
public sealed class MemberNotNullAttribute : Attribute
{
    public MemberNotNullAttribute(string member) => MemberNames = [member];

    public MemberNotNullAttribute(params string[] members) => MemberNames = members;

    public string[] MemberNames { get; }
}

/// <summary>The listed members may be null when the method returns.</summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Property,
    Inherited = false,
    AllowMultiple = true)]
public sealed class MemberMayBeNullAttribute : Attribute
{
    public MemberMayBeNullAttribute(string member) => MemberNames = [member];

    public MemberMayBeNullAttribute(params string[] members) => MemberNames = members;

    public string[] MemberNames { get; }
}

#endif