using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>A metamethod call an instruction needs: the function and up to three arguments.</summary>
internal struct MetaCall
{
    public LuaValue Fn, A, B, C;
    public int N;

    public MetaCall(LuaValue fn, LuaValue a, LuaValue b, LuaValue c, int n)
    {
        Fn = fn;
        A = a;
        B = b;
        C = c;
        N = n;
    }
}

/// <summary>
/// Metamethod dispatch: indexing, assignment, arithmetic, comparison, length and equality. Each operation has a
/// <c>Prepare</c> form that only resolves which metamethod to call (so the VM loop can push a Frame for it and stay yieldable)
/// and a convenience form that also calls it on a nested loop (non-yieldable).
/// </summary>
internal static class Meta
{
    /// <summary>Longest __index/__newindex chain followed before assuming a loop (PUC's MAXTAGLOOP).</summary>
    private const int MaxTagLoop = 2000;

    public static LuaTable? GetMetatable(LuaValue v) => v.Ref is LuaTable t ? t.Metatable : null;

    public static LuaValue GetTagMethod(LuaValue v, TagMethod e)
    {
        var mt = GetMetatable(v);
        return mt is null ? default : mt.GetTagMethod(e);
    }

    private static string At(Proto? p, int pc) => p is null ? "" : Interpreter.Where(p, pc) + " ";

    private static LuaException IndexError(LuaValue v, Proto? p, int pc, bool original)
    {
        string info = "";
        if (original && p is not null)
        {
            var i = p.Code[pc];
            info = VarInfo.Describe(p, pc, i.Op == OpCode.SetTable ? i.A : i.B);
        }
        return new LuaException($"{At(p, pc)}attempt to index a {v.TypeName} value{info}");
    }

    // ---- indexing ----

    /// <summary>Full <c>t[key]</c> read: raw lookup, then the __index chain.</summary>
    public static LuaValue Index(LuaState state, LuaValue t, LuaValue key, Proto? p, int pc) =>
        PrepareIndex(t, key, p, pc, out LuaValue v, out MetaCall mc) ? CallMeta(state, in mc) : v;

    /// <summary>Calls a prepared metamethod on a nested loop and returns its first result.</summary>
    public static LuaValue CallMeta(LuaState state, in MetaCall mc) => Interpreter.Call1(state, mc.Fn, mc.A, mc.B, mc.C, mc.N);

    /// <summary>Resolves <c>t[key]</c>: returns false with the value, or true with the __index function to call.</summary>
    public static bool PrepareIndex(LuaValue t, LuaValue key, Proto? p, int pc, out LuaValue result, out MetaCall mc)
    {
        mc = default;
        result = default;
        for (int loop = 0; loop < MaxTagLoop; loop++)
        {
            LuaValue tm;
            if (t.Ref is LuaTable tbl)
            {
                LuaValue v = tbl.Get(key);
                if (!v.IsNil)
                {
                    result = v;
                    return false;
                }
                var mt = tbl.Metatable;
                if (mt is null) return false;
                tm = mt.GetTagMethod(TagMethod.Index);
                if (tm.IsNil) return false;
            }
            else
            {
                tm = GetTagMethod(t, TagMethod.Index);
                if (tm.IsNil) throw IndexError(t, p, pc, loop == 0);
            }
            if (tm.IsFunction)
            {
                mc = new MetaCall(tm, t, key, default, 2);
                return true;
            }
            t = tm;
        }
        throw new LuaException($"{At(p, pc)}'__index' chain too long; possible loop");
    }

    /// <summary>Raw store with positioned errors for invalid keys.</summary>
    public static void RawSet(LuaTable t, LuaValue key, LuaValue value, Proto? p, int pc)
    {
        if (key.IsNil) throw new LuaException($"{At(p, pc)}table index is nil");
        if (key.IsFloat && double.IsNaN(key.AsFloat)) throw new LuaException($"{At(p, pc)}table index is NaN");
        t.Set(key, value);
    }

    /// <summary>Full <c>t[key] = value</c>: raw store when allowed, otherwise the __newindex chain.</summary>
    public static void SetIndex(LuaState state, LuaValue t, LuaValue key, LuaValue value, Proto? p, int pc)
    {
        if (PrepareSetIndex(t, key, value, p, pc, out MetaCall mc)) CallMeta(state, in mc);
    }

    /// <summary>Does the store when no function is involved (returns false), else returns true with the __newindex call.</summary>
    public static bool PrepareSetIndex(LuaValue t, LuaValue key, LuaValue value, Proto? p, int pc, out MetaCall mc)
    {
        mc = default;
        for (int loop = 0; loop < MaxTagLoop; loop++)
        {
            LuaValue tm;
            if (t.Ref is LuaTable tbl)
            {
                var mt = tbl.Metatable;
                if (mt is null || !tbl.Get(key).IsNil || (tm = mt.GetTagMethod(TagMethod.NewIndex)).IsNil)
                {
                    RawSet(tbl, key, value, p, pc);
                    return false;
                }
            }
            else
            {
                tm = GetTagMethod(t, TagMethod.NewIndex);
                if (tm.IsNil) throw IndexError(t, p, pc, loop == 0);
            }
            if (tm.IsFunction)
            {
                mc = new MetaCall(tm, t, key, value, 3);
                return true;
            }
            t = tm;
        }
        throw new LuaException($"{At(p, pc)}'__newindex' chain too long; possible loop");
    }

    // ---- operators ----

    public static TagMethod ArithEvent(OpCode op) => op switch
    {
        OpCode.Add => TagMethod.Add,
        OpCode.Sub => TagMethod.Sub,
        OpCode.Mul => TagMethod.Mul,
        OpCode.Div => TagMethod.Div,
        OpCode.IDiv => TagMethod.IDiv,
        OpCode.Mod => TagMethod.Mod,
        OpCode.Pow => TagMethod.Pow,
        OpCode.BAnd => TagMethod.BAnd,
        OpCode.BOr => TagMethod.BOr,
        OpCode.BXor => TagMethod.BXor,
        OpCode.Shl => TagMethod.Shl,
        OpCode.Shr => TagMethod.Shr,
        _ => TagMethod.Unm,
    };

    /// <summary>Binary arithmetic where an operand is not a number: try the metamethods, else raise the error.</summary>
    public static LuaValue Arith(LuaState state, TagMethod ev, LuaValue a, LuaValue b, Proto? p, int pc) =>
        CallMeta(state, PrepareArith(ev, a, b, p, pc));

    public static MetaCall PrepareArith(TagMethod ev, LuaValue a, LuaValue b, Proto? p, int pc)
    {
        LuaValue tm = GetTagMethod(a, ev);
        if (tm.IsNil) tm = GetTagMethod(b, ev);
        if (tm.IsNil)
        {
            bool left = !a.IsNumber;
            LuaValue bad = left ? a : b;
            throw new LuaException($"{At(p, pc)}attempt to perform {OperationName(ev)} on a {bad.TypeName} value{VarInfo.DescribeOperand(p, pc, left)}");
        }
        return new MetaCall(tm, a, b, default, 2);
    }

    private static string OperationName(TagMethod ev) =>
        ev is >= TagMethod.BAnd and <= TagMethod.Shr or TagMethod.BNot ? "bitwise operation" : "arithmetic";

    public static LuaValue Unm(LuaState state, LuaValue a, Proto? p, int pc) => Unary(state, TagMethod.Unm, a, p, pc);

    public static LuaValue Unary(LuaState state, TagMethod ev, LuaValue a, Proto? p, int pc) =>
        CallMeta(state, PrepareUnary(ev, a, p, pc));

    public static MetaCall PrepareUnary(TagMethod ev, LuaValue a, Proto? p, int pc)
    {
        LuaValue tm = GetTagMethod(a, ev);
        if (tm.IsNil) throw new LuaException($"{At(p, pc)}attempt to perform {OperationName(ev)} on a {a.TypeName} value{VarInfo.DescribeOperand(p, pc, true)}");
        return new MetaCall(tm, a, a, default, 2);
    }

    public static LuaValue Len(LuaState state, LuaValue v, Proto? p, int pc) =>
        PrepareLen(v, p, pc, out LuaValue res, out MetaCall mc) ? CallMeta(state, in mc) : res;

    public static bool PrepareLen(LuaValue v, Proto? p, int pc, out LuaValue result, out MetaCall mc)
    {
        mc = default;
        if (v.Ref is LuaTable t)
        {
            var mt = t.Metatable;
            LuaValue tm = mt is null ? default : mt.GetTagMethod(TagMethod.Len);
            if (tm.IsNil)
            {
                result = LuaValue.FromInteger(t.Length());
                return false;
            }
            result = default;
            mc = new MetaCall(tm, v, v, default, 2);
            return true;
        }
        if (v.Ref is string s)
        {
            result = LuaValue.FromInteger(s.Length);
            return false;
        }
        result = default;
        throw new LuaException($"{At(p, pc)}attempt to get length of a {v.TypeName} value{VarInfo.DescribeOperand(p, pc, true)}");
    }

    public static bool Equal(LuaState state, LuaValue a, LuaValue b) =>
        PrepareEqual(a, b, out bool eq, out MetaCall mc) ? !CallMeta(state, in mc).IsFalsy : eq;

    /// <summary>Raw equality (returns false), or true with the __eq call whose truthiness is the answer.</summary>
    public static bool PrepareEqual(LuaValue a, LuaValue b, out bool result, out MetaCall mc)
    {
        mc = default;
        result = LuaValue.RawEquals(a, b);
        if (result || a.Ref is not LuaTable ta || b.Ref is not LuaTable tb) return false;
        LuaValue tm = ta.Metatable is null ? default : ta.Metatable.GetTagMethod(TagMethod.Eq);
        if (tm.IsNil) tm = tb.Metatable is null ? default : tb.Metatable.GetTagMethod(TagMethod.Eq);
        if (tm.IsNil) return false;
        mc = new MetaCall(tm, a, b, default, 2);
        return true;
    }

    /// <summary>Order comparison where an operand is not a number: __lt / __le, else the comparison error.</summary>
    public static bool Order(LuaState state, TagMethod ev, LuaValue a, LuaValue b, Proto? p, int pc) =>
        !CallMeta(state, PrepareOrder(ev, a, b, p, pc)).IsFalsy;

    public static MetaCall PrepareOrder(TagMethod ev, LuaValue a, LuaValue b, Proto? p, int pc)
    {
        LuaValue tm = GetTagMethod(a, ev);
        if (tm.IsNil) tm = GetTagMethod(b, ev);
        if (tm.IsNil) throw Compare.Error(a, b, p, pc);
        return new MetaCall(tm, a, b, default, 2);
    }

    // ---- tostring ----

    public static string ToStringValue(LuaState state, LuaValue v)
    {
        var mt = GetMetatable(v);
        if (mt is not null)
        {
            LuaValue tm = mt.Get(ToStringKey);
            if (!tm.IsNil)
            {
                LuaValue r = Interpreter.Call1(state, tm, v, default, default, 1);
                if (r.Ref is string s) return s;
                if (r.IsNumber) return r.ToString();
                throw new LuaException("'__tostring' must return a string");
            }
            LuaValue name = mt.Get(NameKey);
            if (name.Ref is string n) return $"{n}: 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v.Ref):x8}";
        }
        return v.ToString();
    }

    private static readonly LuaValue ToStringKey = LuaValue.FromString("__tostring");
    private static readonly LuaValue NameKey = LuaValue.FromString("__name");
}
