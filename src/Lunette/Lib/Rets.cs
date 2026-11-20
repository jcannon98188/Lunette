namespace Lunette;

/// <summary>
/// Reusable result arrays for builtins that return one to three values, so a builtin call allocates nothing for its
/// result list. The array is valid only until the caller has copied it out: <see cref="BuiltinFunction.Invoke"/>
/// callers consume it immediately and then call <see cref="Release"/>, which clears the slots so the cache never keeps
/// a Lua value alive (weak tables, ADR 0006). Per thread, because a Lua state is single-threaded but a process may host many.
/// </summary>
internal static class Rets
{
    [ThreadStatic] private static LuaValue[]? t_one;
    [ThreadStatic] private static LuaValue[]? t_two;
    [ThreadStatic] private static LuaValue[]? t_three;

    public static LuaValue[] One(LuaValue a)
    {
        var r = t_one ??= new LuaValue[1];
        r[0] = a;
        return r;
    }

    public static LuaValue[] Two(LuaValue a, LuaValue b)
    {
        var r = t_two ??= new LuaValue[2];
        r[0] = a;
        r[1] = b;
        return r;
    }

    public static LuaValue[] Three(LuaValue a, LuaValue b, LuaValue c)
    {
        var r = t_three ??= new LuaValue[3];
        r[0] = a;
        r[1] = b;
        r[2] = c;
        return r;
    }

    /// <summary>The shared array for one to three values (a copy of <paramref name="values"/>), else a fresh array.</summary>
    public static LuaValue[] Of(ReadOnlySpan<LuaValue> values) => values.Length switch
    {
        0 => [],
        1 => One(values[0]),
        2 => Two(values[0], values[1]),
        3 => Three(values[0], values[1], values[2]),
        _ => values.ToArray(),
    };

    /// <summary>Clears <paramref name="res"/> if it is one of the shared arrays; call after copying the results out.</summary>
    public static void Release(LuaValue[]? res)
    {
        if (res is null) return;
        // One thread-static read for the length that matters, no Array.Clear call for the common one-value array.
        switch (res.Length)
        {
            case 1:
                if (ReferenceEquals(res, t_one)) res[0] = default;
                break;
            case 2:
                if (ReferenceEquals(res, t_two)) { res[0] = default; res[1] = default; }
                break;
            case 3:
                if (ReferenceEquals(res, t_three)) Array.Clear(res);
                break;
        }
    }

    /// <summary>A private copy of <paramref name="res"/> when it is a shared array (for results that outlive the call).</summary>
    public static LuaValue[] Own(LuaValue[] res)
    {
        if (res.Length > 3) return res;
        if (!ReferenceEquals(res, t_one) && !ReferenceEquals(res, t_two) && !ReferenceEquals(res, t_three)) return res;
        var copy = new LuaValue[res.Length];
        res.CopyTo(copy, 0);
        Array.Clear(res);
        return copy;
    }
}
