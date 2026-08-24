// Polyfill: `init` accessory vyžadují IsExternalInit, který netstandard2.0 nemá v BCL.
// Bez tohohle by se atributy s `{ get; init; }` nepřeložily pro Unity-safe target.

using System.ComponentModel;

namespace System.Runtime.CompilerServices
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static class IsExternalInit { }
}
