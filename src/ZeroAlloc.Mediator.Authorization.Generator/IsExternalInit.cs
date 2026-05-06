// Polyfill for the netstandard2.0 target — records' init-only setters require this type.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
