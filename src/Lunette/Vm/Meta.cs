using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>Metamethod dispatch: indexing, assignment, arithmetic, comparison, length and equality.</summary>
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

    private static LuaException IndexError(LuaValue v, Proto? p, int pc) =>
        new($"{At(p, pc)}attempt to index a {v.TypeName} value");

    // ---- indexing ----

    /// <summary>Full <c>t[key]</c> read: raw lookup, then the __index chain.</summary>
    public static LuaValue Index(LuaState state, LuaValue t, LuaValue key, Proto? p, int pc)
    {
        for (int loop = 0; loop < MaxTagLoop; loop++)
        {
            LuaValue tm;
            if (t.Ref is LuaTable tbl)
            {
                LuaValue v = tbl.Get(key);
                if (!v.IsNil) return v;
                var mt = tbl.Metatable;
                if (mt is null) return default;
                tm = mt.GetTagMethod(TagMethod.Index);
                if (tm.IsNil) return default;
            }
            else
            {
                tm = GetTagMethod(t, TagMethod.Index);
                if (tm.IsNil) throw IndexError(t, p, pc);
            }
            if (tm.IsFunction) return Interpreter.Call1(state, tm, t, key, default, 2);
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
        for (int loop = 0; loop < MaxTagLoop; loop++)
        {
            LuaValue tm;
            if (t.Ref is LuaTable tbl)
            {
                var mt = tbl.Metatable;
                if (mt is null || !tbl.Get(key).IsNil || (tm = mt.GetTagMethod(TagMethod.NewIndex)).IsNil)
                {
                    RawSet(tbl, key, value, p, pc);
                    return;
                }
            }
            else
            {
                tm = GetTagMethod(t, TagMethod.NewIndex);
                if (tm.IsNil) throw IndexError(t, p, pc);
            }
            if (tm.IsFunction)
            {
                Interpreter.Call1(state, tm, t, key, value, 3);
                return;
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
        _ => TagMethod.Unm,
    };

    /// <summary>Binary arithmetic where an operand is not a number: try the metamethods, else raise the error.</summary>
    public static LuaValue Arith(LuaState state, TagMethod ev, LuaValue a, LuaValue b, Proto? p, int pc)
    {
        LuaValue tm = GetTagMethod(a, ev);
        if (tm.IsNil) tm = GetTagMethod(b, ev);
        if (tm.IsNil)
        {
            LuaValue bad = a.IsNumber ? b : a;
            throw new LuaException($"{At(p, pc)}attempt to perform arithmetic on a {bad.TypeName} value");
        }
        return Interpreter.Call1(state, tm, a, b, default, 2);
    }

    public static LuaValue Unm(LuaState state, LuaValue a, Proto? p, int pc)
    {
        LuaValue tm = GetTagMethod(a, TagMethod.Unm);
        if (tm.IsNil) throw new LuaException($"{At(p, pc)}attempt to perform arithmetic on a {a.TypeName} value");
        return Interpreter.Call1(state, tm, a, a, default, 2);
    }

    public static LuaValue Len(LuaState state, LuaValue v, Proto? p, int pc)
    {
        if (v.Ref is LuaTable t)
        {
            var mt = t.Metatable;
            if (mt is null) return LuaValue.FromInteger(t.Length());
            LuaValue tm = mt.GetTagMethod(TagMethod.Len);
            if (tm.IsNil) return LuaValue.FromInteger(t.Length());
            return Interpreter.Call1(state, tm, v, v, default, 2);
        }
        if (v.Ref is string s) return LuaValue.FromInteger(s.Length);
        throw new LuaException($"{At(p, pc)}attempt to get length of a {v.TypeName} value");
    }

    public static bool Equal(LuaState state, LuaValue a, LuaValue b)
    {
        if (LuaValue.RawEquals(a, b)) return true;
        if (a.Ref is not LuaTable ta || b.Ref is not LuaTable tb) return false;
        LuaValue tm = ta.Metatable is null ? default : ta.Metatable.GetTagMethod(TagMethod.Eq);
        if (tm.IsNil) tm = tb.Metatable is null ? default : tb.Metatable.GetTagMethod(TagMethod.Eq);
        if (tm.IsNil) return false;
        return !Interpreter.Call1(state, tm, a, b, default, 2).IsFalsy;
    }

    /// <summary>Order comparison where an operand is not a number: __lt / __le, else the comparison error.</summary>
    public static bool Order(LuaState state, TagMethod ev, LuaValue a, LuaValue b, Proto? p, int pc)
    {
        LuaValue tm = GetTagMethod(a, ev);
        if (tm.IsNil) tm = GetTagMethod(b, ev);
        if (tm.IsNil) throw Compare.Error(a, b, p, pc);
        return !Interpreter.Call1(state, tm, a, b, default, 2).IsFalsy;
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
