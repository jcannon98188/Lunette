namespace Lunette.Unsafe;

// Safe twin of Regs.cs, compiled instead of it under LUA_SAFE_ACCESS (dotnet test -c Release -p:LuaSafeAccess=true; nightly
// `safe-twin` job). Same type, same member names and signatures, same observable behaviour; no unsafe code, no
// System.Runtime.CompilerServices.Unsafe, no MemoryMarshal (LD005 still holds), every access bounds-checked and every store
// going through the ordinary struct assignment (so the GC write barrier is always run).
//
// Maintenance rule: Regs.cs is excluded from the build under the define (see src/Lunette/Lunette.csproj), so a helper added
// to Regs.cs without a twin here fails the safe build at its call sites. Add the twin member below with the same signature.
//
// Why the unsafe helpers are sound (ADR 0001, ADR 0006), and what this twin must preserve:
//  * ADR 0001: a LuaValue is {object? Ref; long Bits}; the type is derived from Ref and number payloads live in Bits. A
//    numeric store that finds the destination already holding the same tag object changes only the 8-byte non-reference
//    field, so there is no reference store and the write barrier has nothing to record. If the tag differs the helpers
//    store the whole struct (or the Ref through a normal, barriered reference write). The twin simply always does the full
//    barriered store, which is observably identical.
//  * ADR 0006: weak entries are weakened only inside a collection window, which runs on the Lua thread at a safepoint, never
//    concurrently with the dispatch loop. A skipped barrier therefore cannot hide a young-object reference from a
//    concurrent or background mark: the scalar tags are string literals on the frozen heap (never collected, never moved, not
//    in any generation), so storing one creates no reference the collector must learn about, and a payload write creates none either.
//    Store and Copy write a tag constant only when the source tag is one of the four scalar tags; every other reference goes through a
//    normal barriered reference store. If a runtime ever stopped freezing the literals the JIT would simply emit the barrier again.
//    Stale-slot clearing at a collection operates on the same arrays and sees the same values either way.
//  * Register indexes come from the compiler and are below the frame's MaxStack, which the call sequence reserved; the
//    unsafe At drops the bounds check on that basis. The twin keeps the check, so a violation of that invariant surfaces
//    as an IndexOutOfRangeException under the variant instead of memory corruption.
internal static class Regs
{
    /// <summary>A reference to element <paramref name="i"/> of <paramref name="a"/> (bounds-checked).</summary>
    public static ref LuaValue At(LuaValue[] a, int i) => ref a[i];

    /// <summary>Register <paramref name="n"/> of frame <paramref name="f"/> (bounds-checked; ignores the base reference the unsafe version uses).</summary>
    public static ref LuaValue Reg(Vm.Coroutine co, Vm.Frame f, ref LuaValue rb, int n) => ref co.Stack[f.Base + n];

    /// <summary>True for nil and false (see <see cref="LuaValue.IsFalsy"/>).</summary>
    public static bool IsFalsy(ref LuaValue v) => v.IsFalsy;

    /// <summary>Stores an integer into <paramref name="dst"/>.</summary>
    public static void SetInteger(ref LuaValue dst, long v) => dst = LuaValue.FromInteger(v);

    /// <summary>Overwrites the payload of a register that already holds a number, keeping its number type.</summary>
    public static void SetBits(ref LuaValue dst, long bits) =>
        dst = dst.IsFloat ? LuaValue.FromFloat(BitConverter.Int64BitsToDouble(bits)) : LuaValue.FromInteger(bits);

    /// <summary>Stores a float into <paramref name="dst"/>.</summary>
    public static void SetFloat(ref LuaValue dst, double v) => dst = LuaValue.FromFloat(v);

    /// <summary>Copies <paramref name="src"/> into <paramref name="dst"/>.</summary>
    public static void Copy(ref LuaValue dst, in LuaValue src) => dst = src;

    /// <summary>Stores into a heap slot (table slot, upvalue).</summary>
    public static void Store(ref LuaValue dst, in LuaValue src) => dst = src;
}
