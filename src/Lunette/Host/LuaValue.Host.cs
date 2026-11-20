using System.Diagnostics.CodeAnalysis;
using System.Text.Unicode;

namespace Lunette;

/// <summary>The basic Lua types as the host sees them.</summary>
public enum LuaType
{
    Nil,
    Boolean,
    Number,
    String,
    Table,
    Function,
    Userdata,
    Thread,
}

/// <summary>Host-facing conversions. Strings cross the boundary as UTF-8 (explicit Latin-1 is available); no Lua semantics depend on it.</summary>
public readonly partial struct LuaValue
{
    public static implicit operator LuaValue(bool v) => FromBoolean(v);
    public static implicit operator LuaValue(long v) => FromInteger(v);
    public static implicit operator LuaValue(int v) => FromInteger(v);
    public static implicit operator LuaValue(double v) => FromFloat(v);
    public static implicit operator LuaValue(string? v) => v is null ? default : FromString(v);
    public static LuaValue FromFunction(LuaFunction f) => FromObject(f);
    public static LuaValue FromUserdata(LuaUserdata u) => FromObject(u);

    /// <summary>A Lua string holding the UTF-8 encoding of <paramref name="s"/> (unpaired surrogates become U+FFFD).</summary>
    public static LuaValue FromString(string s) => FromString(LuaString.FromUtf16(s));

    /// <summary>A Lua string holding a copy of the given bytes, which need not be valid UTF-8.</summary>
    public static LuaValue FromBytes(ReadOnlySpan<byte> bytes) => FromString(LuaString.Create(bytes));

    /// <summary>A Lua string with one byte per char (each char must be 0-255), the explicit raw-byte conversion.</summary>
    public static LuaValue FromLatin1(string s) => FromString(LuaString.FromLatin1(s));

    /// <summary>The Lua type of this value.</summary>
    public LuaType Type =>
        Ref switch
        {
            null => LuaType.Nil,
            _ when IsNumber => LuaType.Number,
            _ when IsBoolean => LuaType.Boolean,
            byte[] => LuaType.String,
            LuaTable => LuaType.Table,
            LuaFunction => LuaType.Function,
            Vm.Coroutine => LuaType.Thread,
            _ => LuaType.Userdata,
        };

    public bool TryGetInteger(out long value)
    {
        value = Bits;
        return IsInteger;
    }

    /// <summary>Succeeds for integers and floats; integers convert to the nearest double.</summary>
    public bool TryGetNumber(out double value) => TryToNumber(out value);

    public bool TryGetBoolean(out bool value)
    {
        value = ReferenceEquals(Ref, TrueTag);
        return IsBoolean;
    }

    /// <summary>Zero-copy view of a string's bytes, valid as long as the value is.</summary>
    public bool TryGetBytes(out ReadOnlySpan<byte> bytes)
    {
        if (Ref is byte[] raw)
        {
            bytes = new LuaString(raw).Bytes;
            return true;
        }
        bytes = default;
        return false;
    }

    /// <summary>Decodes a string as UTF-8; invalid sequences become U+FFFD (use <see cref="TryGetStrictString"/> to reject them).</summary>
    public bool TryGetString([NotNullWhen(true)] out string? value)
    {
        if (Ref is byte[] raw)
        {
            value = new LuaString(raw).ToString();
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Decodes a string as UTF-8, failing (false) when it is not a string or not valid UTF-8.</summary>
    public bool TryGetStrictString([NotNullWhen(true)] out string? value)
    {
        value = null;
        if (Ref is not byte[] raw) return false;
        ReadOnlySpan<byte> b = new LuaString(raw).Bytes;
        var chars = new char[b.Length];
        if (Utf8.ToUtf16(b, chars, out _, out int written, replaceInvalidSequences: false, isFinalBlock: true) != System.Buffers.OperationStatus.Done)
            return false;
        value = new string(chars, 0, written);
        return true;
    }

    /// <summary>One char per byte, the explicit raw-byte counterpart of <see cref="FromLatin1"/>.</summary>
    public bool TryGetLatin1String([NotNullWhen(true)] out string? value)
    {
        if (Ref is byte[] raw)
        {
            value = new LuaString(raw).ToLatin1String();
            return true;
        }
        value = null;
        return false;
    }

    /// <summary>Typed accessor: long, double, bool, string, or any reference type the value holds (<see cref="LuaTable"/>, <see cref="LuaFunction"/>, <see cref="LuaUserdata"/>).</summary>
    public bool TryGet<T>([MaybeNullWhen(false)] out T value)
    {
        if (typeof(T) == typeof(long))
        {
            bool ok = TryGetInteger(out long l);
            value = ok ? (T)(object)l : default;
            return ok;
        }
        if (typeof(T) == typeof(double))
        {
            bool ok = TryGetNumber(out double d);
            value = ok ? (T)(object)d : default;
            return ok;
        }
        if (typeof(T) == typeof(bool))
        {
            bool ok = TryGetBoolean(out bool b);
            value = ok ? (T)(object)b : default;
            return ok;
        }
        if (typeof(T) == typeof(string))
        {
            bool ok = TryGetString(out string? s);
            value = ok ? (T)(object)s! : default;
            return ok;
        }
        if (Ref is T t && Ref is not byte[])
        {
            value = t;
            return true;
        }
        value = default;
        return false;
    }
}
