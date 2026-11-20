using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lunette.Unsafe;

/// <summary>A mutable view of <see cref="LuaValue"/> with the same two fields in the same order (a test checks the layouts agree).</summary>
internal struct RawValue
{
    public object? Ref;
    public long Bits;
}

/// <summary>
/// Register access for the dispatch loop (the one place the VM uses unsafe code, LD005). Register indexes come from the compiler and
/// are always below the frame's <c>MaxStack</c>, which the call sequence has reserved, so the bounds check is dropped (and asserted in Debug).
/// Numeric stores skip the GC write barrier when the destination already holds a value of the same number type: only the
/// 8-byte payload changes and the type tag object stays the same.
/// Why this is sound with respect to ADR 0001 and ADR 0006 is written up in RegsSafe.cs, which is the bounds-checked, always-barriered twin
/// compiled instead of this file under LUA_SAFE_ACCESS. Every helper added here needs a twin there.
/// </summary>
internal static class Regs
{
    /// <summary>A reference to element <paramref name="i"/> of <paramref name="a"/>, with no bounds check.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref LuaValue At(LuaValue[] a, int i)
    {
        Debug.Assert((uint)i < (uint)a.Length);
        return ref System.Runtime.CompilerServices.Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(a), (nint)i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref RawValue Raw(ref LuaValue v) => ref System.Runtime.CompilerServices.Unsafe.As<LuaValue, RawValue>(ref v);

    /// <summary>True for nil and false (the same test as <see cref="LuaValue.IsFalsy"/>, read through the raw view).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFalsy(ref LuaValue v)
    {
        object? tag = Raw(ref v).Ref;
        return tag is null || ReferenceEquals(tag, LuaValue.FalseTag);
    }

    /// <summary>Stores an integer into <paramref name="dst"/>; no write barrier when it already holds an integer.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetInteger(ref LuaValue dst, long v)
    {
        ref RawValue raw = ref Raw(ref dst);
        if (!ReferenceEquals(raw.Ref, LuaValue.IntegerTag)) raw.Ref = LuaValue.IntegerTag;
        raw.Bits = v;
    }

    /// <summary>Overwrites the payload of a register that already holds a number of the type being stored (the hidden state of a numeric for loop).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetBits(ref LuaValue dst, long bits) => Raw(ref dst).Bits = bits;

    /// <summary>Stores a float into <paramref name="dst"/>; no write barrier when it already holds a float.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetFloat(ref LuaValue dst, double v)
    {
        ref RawValue raw = ref Raw(ref dst);
        if (!ReferenceEquals(raw.Ref, LuaValue.FloatTag)) raw.Ref = LuaValue.FloatTag;
        raw.Bits = BitConverter.DoubleToInt64Bits(v);
    }

    /// <summary>Copies <paramref name="src"/> into <paramref name="dst"/>; no write barrier when both hold the same object (the common numeric case).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Copy(ref LuaValue dst, in LuaValue src)
    {
        ref RawValue d = ref Raw(ref dst);
        ref readonly RawValue s = ref System.Runtime.CompilerServices.Unsafe.As<LuaValue, RawValue>(ref System.Runtime.CompilerServices.Unsafe.AsRef(in src));
        if (ReferenceEquals(d.Ref, s.Ref)) d.Bits = s.Bits;
        else dst = src;
    }
}
