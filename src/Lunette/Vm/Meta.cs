using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>Metamethod dispatch: indexing, assignment, arithmetic, comparison, length and equality.</summary>
internal static class Meta
{
    /// <summary>Longest __index/__newindex chain followed before assuming a loop (PUC's MAXTAGLOOP).</summary>
    private const int MaxTagLoop = 2000;

    public static LuaTable? GetMetatable(LuaState state, LuaValue v) =>
        v.Ref is LuaTable t ? t.Metatable : v.Ref is byte[] ? state.StringMetatable : null;

    public static LuaValue GetTagMethod(LuaState state, LuaValue v, TagMethod e)
    {
        var mt = GetMetatable(state, v);
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
                tm = GetTagMethod(state, t, TagMethod.Index);
                if (tm.IsNil) throw IndexError(t, p, pc, loop == 0);
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
                tm = GetTagMethod(state, t, TagMethod.NewIndex);
                if (tm.IsNil) throw IndexError(t, p, pc, loop == 0);
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
        OpCode.BAnd => TagMethod.BAnd,
        OpCode.BOr => TagMethod.BOr,
        OpCode.BXor => TagMethod.BXor,
        OpCode.Shl => TagMethod.Shl,
        OpCode.Shr => TagMethod.Shr,
        _ => TagMethod.Unm,
    };

    /// <summary>Binary arithmetic where an operand is not a number: try the metamethods, else raise the error.</summary>
    public static LuaValue Arith(LuaState state, TagMethod ev, LuaValue a, LuaValue b, Proto? p, int pc)
    {
        // Manual 3.4.3: strings convert to numbers in arithmetic (PUC does this through string-metatable metamethods;
        // observably the same unless the other operand has its own metamethod, which still wins below).
        if (ev is >= TagMethod.Add and <= TagMethod.IDiv
            && StringConv.TryToNumber(a, out LuaValue na) && StringConv.TryToNumber(b, out LuaValue nb))
            return Vm.Arith.Binary(OpFor(ev), na, nb, p!, pc);
        LuaValue tm = GetTagMethod(state, a, ev);
        if (tm.IsNil) tm = GetTagMethod(state, b, ev);
        if (tm.IsNil)
        {
            bool left = !a.IsNumber;
            LuaValue bad = left ? a : b;
            throw new LuaException($"{At(p, pc)}attempt to perform {OperationName(ev)} on a {bad.TypeName} value{VarInfo.DescribeOperand(p, pc, left)}");
        }
        return Interpreter.Call1(state, tm, a, b, default, 2);
    }

    private static OpCode OpFor(TagMethod ev) => ev switch
    {
        TagMethod.Add => OpCode.Add,
        TagMethod.Sub => OpCode.Sub,
        TagMethod.Mul => OpCode.Mul,
        TagMethod.Mod => OpCode.Mod,
        TagMethod.Pow => OpCode.Pow,
        TagMethod.Div => OpCode.Div,
        _ => OpCode.IDiv,
    };

    private static string OperationName(TagMethod ev) =>
        ev is >= TagMethod.BAnd and <= TagMethod.Shr or TagMethod.BNot ? "bitwise operation" : "arithmetic";

    public static LuaValue Unm(LuaState state, LuaValue a, Proto? p, int pc) => Unary(state, TagMethod.Unm, a, p, pc);

    public static LuaValue Unary(LuaState state, TagMethod ev, LuaValue a, Proto? p, int pc)
    {
        if (ev == TagMethod.Unm && StringConv.TryToNumber(a, out LuaValue n))
            return Vm.Arith.Negate(n, p!, pc);
        LuaValue tm = GetTagMethod(state, a, ev);
        if (tm.IsNil) throw new LuaException($"{At(p, pc)}attempt to perform {OperationName(ev)} on a {a.TypeName} value{VarInfo.DescribeOperand(p, pc, true)}");
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
        if (v.Ref is byte[] s) return LuaValue.FromInteger(new LuaString(s).Length);
        throw new LuaException($"{At(p, pc)}attempt to get length of a {v.TypeName} value{VarInfo.DescribeOperand(p, pc, true)}");
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
        if (a.Ref is byte[] sa && b.Ref is byte[] sb)
        {
            int cmp = new LuaString(sa).CompareTo(new LuaString(sb));
            return ev == TagMethod.Lt ? cmp < 0 : cmp <= 0;
        }
        LuaValue tm = GetTagMethod(state, a, ev);
        if (tm.IsNil) tm = GetTagMethod(state, b, ev);
        if (tm.IsNil) throw Compare.Error(a, b, p, pc);
        return !Interpreter.Call1(state, tm, a, b, default, 2).IsFalsy;
    }

    // ---- concatenation ----

    public static LuaValue Concat(LuaState state, LuaValue a, LuaValue b, Proto? p, int pc)
    {
        if ((a.IsString || a.IsNumber) && (b.IsString || b.IsNumber)) return StringConv.Concat(state, a, b);
        LuaValue tm = GetTagMethod(state, a, TagMethod.Concat);
        if (tm.IsNil) tm = GetTagMethod(state, b, TagMethod.Concat);
        if (tm.IsNil)
        {
            LuaValue bad = a.IsString || a.IsNumber ? b : a;
            throw new LuaException($"{At(p, pc)}attempt to concatenate a {bad.TypeName} value");
        }
        return Interpreter.Call1(state, tm, a, b, default, 2);
    }

    // ---- tostring ----

    public static LuaString ToStringValue(LuaState state, LuaValue v)
    {
        var mt = GetMetatable(state, v);
        if (mt is not null)
        {
            LuaValue tm = mt.Get(ToStringKey);
            if (!tm.IsNil)
            {
                LuaValue r = Interpreter.Call1(state, tm, v, default, default, 1);
                if (r.Ref is byte[] s) return new LuaString(s);
                if (r.IsNumber) return StringConv.NumberToString(state, r);
                throw new LuaException("'__tostring' must return a string");
            }
            LuaValue name = mt.Get(NameKey);
            if (name.Ref is byte[] n)
                return state.Strings.FromUtf16($"{new LuaString(n)}: 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v.Ref):x8}");
        }
        if (v.Ref is byte[] self) return new LuaString(self);
        if (v.IsNumber) return StringConv.NumberToString(state, v);
        return state.Strings.FromUtf16(v.ToString());
    }

    private static readonly LuaValue ToStringKey = LuaValue.FromString("__tostring");
    private static readonly LuaValue NameKey = LuaValue.FromString("__name");
}
