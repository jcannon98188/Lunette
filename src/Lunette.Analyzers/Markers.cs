using System;
using Microsoft.CodeAnalysis;

namespace Lunette.Analyzers
{
    /// <summary>
    /// Marker attributes are matched by simple name, so the core assembly declares its own internal copies
    /// (see Lunette/Markers.cs) and the analyzer needs no reference to it.
    /// </summary>
    internal static class Markers
    {
        public const string HotPath = "LuaHotPathAttribute";
        public const string ColdThrow = "LuaColdThrowAttribute";
        public const string StringPath = "LuaStringPathAttribute";

        public static bool IsMarked(ISymbol? symbol, string attributeName)
        {
            for (var s = symbol; s != null; s = s.ContainingSymbol)
            {
                if (HasAttribute(s, attributeName))
                {
                    return true;
                }

                if (s is IMethodSymbol { AssociatedSymbol: { } assoc } && HasAttribute(assoc, attributeName))
                {
                    return true;
                }

                if (s is INamespaceSymbol)
                {
                    break;
                }
            }

            return false;
        }

        public static bool HasAttribute(ISymbol symbol, string attributeName)
        {
            foreach (var attr in symbol.GetAttributes())
            {
                if (string.Equals(attr.AttributeClass?.Name, attributeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
