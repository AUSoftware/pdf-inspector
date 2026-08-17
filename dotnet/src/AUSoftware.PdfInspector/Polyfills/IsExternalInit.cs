#if !NET5_0_OR_GREATER
using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>
/// Marker the C# compiler requires for <c>init</c> accessors. Present in the
/// framework from .NET 5 onwards; declared here so the netstandard2.0 target
/// can use init-only properties too.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}
#endif
