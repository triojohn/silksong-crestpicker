// netstandard2.1 has no IsExternalInit, which record types and init-only setters require.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
#endif
