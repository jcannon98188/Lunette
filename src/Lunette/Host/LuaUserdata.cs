using System.Runtime.CompilerServices;

namespace Lunette;

/// <summary>
/// A Lua userdata wrapping any .NET object. The core never inspects the object: behaviour comes from a host-built
/// metatable (set per value, or looked up by the object's type in <see cref="LuaState.RegisterMetatable"/>).
/// </summary>
public sealed class LuaUserdata
{
    internal LuaUserdata(object value, LuaTable? metatable)
    {
        Value = value;
        Metatable = metatable;
    }

    /// <summary>The wrapped object.</summary>
    public object Value { get; }

    /// <summary>The metatable consulted for indexing, operators and <c>__tostring</c>; null for none.</summary>
    public LuaTable? Metatable { get; set; }

    public override string ToString() => $"userdata: 0x{RuntimeHelpers.GetHashCode(this):x8}";
}
