// ============================================================================
// IsExternalInit.cs  --  net48 polyfill for C# 9 `init` keyword
// ============================================================================
// C# 9's `init` accessor requires a type `System.Runtime.CompilerServices.
// IsExternalInit` to exist in the BCL. On .NET 5+ this type is built in;
// on .NET Framework 4.x it is missing, so the compiler errors with
// `CS0518: Predefined type 'System.Runtime.CompilerServices.IsExternalInit'
// is not defined or imported`.
//
// Drop this file into any assembly that uses `init` setters (or records).
// It MUST be in the global namespace, in the exact namespace the compiler
// is looking for. Multiple definitions across assemblies are fine; the
// compiler picks the first one visible.
//
// Reference: https://github.com/dotnet/roslyn/issues/45510
// ============================================================================

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
