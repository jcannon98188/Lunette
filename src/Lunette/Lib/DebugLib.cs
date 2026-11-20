using System.Text;
using Lunette.Compiler;
using Lunette.Lib;
using Lunette.Vm;

namespace Lunette;

/// <summary>
/// The <c>debug</c> library (ticket 08, Q6): getinfo, traceback, locals, upvalues, hooks, metatables, registry. Call levels follow
/// PUC: level 0 is the running host function, level 1 its caller. A level is a Lua Frame or a host function that has no Frame
/// (the running builtin, pcall, a function whose call or return event a hook reports).
/// </summary>
internal static class DebugLib
{
    private const int IdSize = 60;
    private const int Levels1 = 10, Levels2 = 11;

    private static readonly string[] LibNames = ["string", "table", "math", "os", "io", "coroutine", "utf8", "debug", "package"];

    public static void Register(LuaState state)
    {
        if (!state.Wants("debug")) return;
        var t = new LuaTable(0, 20);
        Add(t, "getinfo", GetInfo);
        Add(t, "traceback", Traceback);
        Add(t, "getlocal", GetLocal);
        Add(t, "setlocal", SetLocal);
        Add(t, "getupvalue", GetUpvalue);
        Add(t, "setupvalue", SetUpvalue);
        Add(t, "upvalueid", UpvalueId);
        Add(t, "upvaluejoin", UpvalueJoin);
        t.Set(LuaValue.FromString("sethook"), LuaValue.FromObject(new BuiltinFunction(SetHook, BuiltinKind.SetHook)));
        Add(t, "gethook", GetHook);
        Add(t, "getmetatable", GetMetatable);
        Add(t, "setmetatable", SetMetatable);
        Add(t, "getregistry", static (st, _) => [LuaValue.FromTable(Registry(st))]);
        Add(t, "getuservalue", GetUserValue);
        Add(t, "setuservalue", SetUserValue);
        Add(t, "debug", DebugPrompt);
        Add(t, "setcstacklimit", static (_, args) =>
        {
            LibArgs.CheckInteger("setcstacklimit", args, 0);
            return [LuaValue.FromInteger(200)];
        });
        state.SetGlobal("debug", LuaValue.FromTable(t));
    }

    private static void Add(LuaTable t, string name, BuiltinBody body) =>
        t.Set(LuaValue.FromString(name), LuaValue.FromObject(new BuiltinFunction(body)));

    /// <summary>The registry table. It carries a weak-keyed _HOOKKEY table as PUC does, although Lunette keeps hooks on the coroutine itself.</summary>
    private static LuaTable Registry(LuaState st)
    {
        if (st.Registry is { } r) return r;
        r = st.Registry = new LuaTable();
        var hooks = new LuaTable();
        var meta = new LuaTable();
        meta.Set(Str("__mode"), Str("k"));
        hooks.Metatable = meta;
        st.Gc.OnSetMetatable(hooks, meta);
        r.Set(Str("_HOOKKEY"), LuaValue.FromTable(hooks));
        return r;
    }

    private static LuaValue Str(string s) => LuaValue.FromString(s);

    // ---- chunk names ----

    /// <summary>The printable form of a chunk name, after luaO_chunkid (LUA_IDSIZE 60).</summary>
    internal static string ChunkId(string source)
    {
        if (source.Length > 0 && source[0] == '=')
            return source.Length <= IdSize ? source[1..] : source.Substring(1, IdSize - 1);
        if (source.Length > 0 && source[0] == '@')
            return source.Length <= IdSize ? source[1..] : "..." + source[^(IdSize - 4)..];
        const string pre = "[string \"", rets = "...", pos = "\"]";
        int nl = source.IndexOf('\n');
        int bufflen = IdSize - (pre.Length + rets.Length + pos.Length) - 1;
        if (source.Length < bufflen && nl < 0) return pre + source + pos;
        int len = nl >= 0 ? nl : source.Length;
        if (len > bufflen) len = bufflen;
        return pre + source[..len] + rets + pos;
    }

    // ---- levels ----

    /// <summary>One call level: a Lua Frame index, or -1 for a host function (whose value is <see cref="Fn"/> when known).</summary>
    internal readonly record struct Level(int Frame, LuaValue Fn);

    private static LuaValue Lookup(LuaState state, string lib, string name)
    {
        if (lib.Length == 0) return state.GetGlobal(name);
        return LibTable(state, lib) is { } t ? t.Get(Str(name)) : default;
    }

    /// <summary>A standard library table: the global, or (a suite may clear it, as all.lua does for debug) the one in package.loaded.</summary>
    private static LuaTable? LibTable(LuaState state, string lib)
    {
        if (state.GetGlobal(lib).Ref is LuaTable t) return t;
        return state.GetGlobal("package").Ref is LuaTable pkg && pkg.Get(Str("loaded")).Ref is LuaTable loaded
            ? loaded.Get(Str(lib)).Ref as LuaTable
            : null;
    }

    internal static List<Level> Levels(LuaState state, Coroutine co, LuaValue self, bool includeSelf)
    {
        var list = new List<Level>();
        if (includeSelf) list.Add(new Level(-1, self));
        bool raiseInline = co.InHandler && !co.RaisingBuiltin.IsNil && co.RaisingDepth <= co.Depth;
        if (!co.RaisingBuiltin.IsNil && !raiseInline && !includeSelf) list.Add(new Level(-1, co.RaisingBuiltin));
        else if (co != state.Running && co.Started && co.Status == CoStatus.Suspended && co.Depth > 0)
            list.Add(new Level(-1, Lookup(state, "coroutine", "yield")));
        bool inHook = !co.AllowHook && co.HookFrameIndex >= 0;
        for (int i = co.Depth - 1; i >= 0; i--)
        {
            if (raiseInline && i == co.RaisingDepth - 1) list.Add(new Level(-1, co.RaisingBuiltin));
            Frame fr = co.Frames[i]!;
            if (i == co.HostProtectDepth - 1) list.Add(new Level(-1, Lookup(state, "", co.HostProtectX ? "xpcall" : "pcall")));
            // A protected call whose __close handlers are running has already unwound: its callee is gone, the handlers' caller is the pcall.
            if (!fr.Closing) list.Add(new Level(i, LuaValue.FromObject(fr.Closure)));
            if (inHook && i == co.HookFrameIndex && !co.HookBuiltin.IsNil) list.Add(new Level(-1, co.HookBuiltin));
            if (fr.PcallKind != 0) list.Add(new Level(-1, Lookup(state, "", fr.PcallKind == 2 ? "xpcall" : "pcall")));
        }
        if (co.CBase) list.Add(new Level(-1, default));
        return list;
    }

    /// <summary>
    /// Records the source line a VM-detected error was raised at. The saved pc of the top Frame is only current at call instructions,
    /// but such an error's message starts with the position of the faulting instruction, which is the line a traceback must show.
    /// </summary>
    internal static void NoteFault(Coroutine co, LuaException ex)
    {
        co.FaultLine = 0;
        if (co.Depth == 0) return;
        string prefix = ChunkId(co.Frames[co.Depth - 1]!.Closure.Proto.ChunkName) + ":";
        string m = ex.Message;
        if (!m.StartsWith(prefix, StringComparison.Ordinal)) return;
        int j = prefix.Length;
        while (j < m.Length && char.IsAsciiDigit(m[j])) j++;
        if (j > prefix.Length && j < m.Length && m[j] == ':' && int.TryParse(m.AsSpan(prefix.Length, j - prefix.Length), out int line))
        {
            co.FaultLine = line;
            co.FaultDepth = co.Depth;
        }
    }

    private static int CurrentLine(Coroutine co, Frame fr, int frameIndex)
    {
        if (co.FaultLine > 0 && co.FaultDepth == frameIndex + 1) return co.FaultLine;
        int[] lines = fr.Closure.Proto.Lines;
        return lines[Math.Clamp(fr.Pc - 1, 0, lines.Length - 1)];
    }

    /// <summary>How the function at <paramref name="i"/> was called, from the calling instruction (ldebug.c funcnamefromcode).</summary>
    private static (string? What, string? Name) NameOf(Coroutine co, List<Level> lv, int i)
    {
        Level l = lv[i];
        if (l.Frame < 0 && co.HookBuiltinNested && !co.AllowHook && ReferenceEquals(l.Fn.Ref, co.HookBuiltin.Ref)) return (null, null);
        Frame? caller = i + 1 < lv.Count && lv[i + 1].Frame >= 0 ? co.Frames[lv[i + 1].Frame] : null;
        if (l.Frame >= 0)
        {
            Frame fr = co.Frames[l.Frame]!;
            if (fr.IsTail) return (null, null);
            if (l.Frame == co.FinalizerFrame) return ("metamethod", "__gc");
            if (!co.AllowHook && l.Frame == co.HookFrameIndex) return ("hook", "?");
            if (fr.PcallKind != 0 || (fr.Native && fr.Cont == Continuation.None)) return (null, null);
            if (caller is null || lv[i + 1].Frame != l.Frame - 1) return (null, null);
            if (fr.Cont != Continuation.None) return MetaName(caller);
        }
        if (caller is null) return (null, null);
        Proto p = caller.Closure.Proto;
        int pc = caller.Pc - 1;
        if (pc < 0 || pc >= p.Code.Length) return (null, null);
        Instruction ins = p.Code[pc];
        if (ins.Op is not (OpCode.Call or OpCode.TailCall)) return l.Frame >= 0 ? MetaName(caller) : (null, null);
        if (ins.Op == OpCode.Call && pc + 1 < p.Code.Length && p.Code[pc + 1].Op == OpCode.TForLoop) return ("for iterator", "for iterator");
        string? kind = VarInfo.ObjName(p, pc, ins.A, out string name);
        return kind is null ? (null, null) : (kind, name);
    }

    private static (string?, string?) MetaName(Frame caller)
    {
        Proto p = caller.Closure.Proto;
        int pc = caller.Pc - 1;
        if (pc < 0 || pc >= p.Code.Length) return (null, null);
        OpCode op = p.Code[pc].Op;
        TagMethod? ev = op switch
        {
            _ when Meta.IsBinaryArith(op) => Meta.ArithEvent(op),
            OpCode.Unm => TagMethod.Unm,
            OpCode.BNot => TagMethod.BNot,
            OpCode.Eq or OpCode.JEq or OpCode.JEqK => TagMethod.Eq,
            OpCode.Lt or OpCode.JLt or OpCode.JLtK or OpCode.JGtK => TagMethod.Lt,
            OpCode.Le or OpCode.JLe or OpCode.JLeK or OpCode.JGeK => TagMethod.Le,
            OpCode.Concat => TagMethod.Concat,
            OpCode.Len => TagMethod.Len,
            OpCode.GetTable or OpCode.Self => TagMethod.Index,
            OpCode.SetTable => TagMethod.NewIndex,
            OpCode.Close or OpCode.Return => TagMethod.Close,
            _ => null,
        };
        return ev is { } e ? ("metamethod", TagMethods.Name(e)[2..]) : (null, null);
    }

    /// <summary>How the function being called by the instruction at the saved pc of <paramref name="caller"/> is named (a call, or a metamethod-raising instruction).</summary>
    internal static (string? What, string? Name) CallSiteName(Frame caller)
    {
        Proto p = caller.Closure.Proto;
        int pc = caller.Pc - 1;
        if (pc < 0 || pc >= p.Code.Length) return (null, null);
        Instruction ins = p.Code[pc];
        if (ins.Op is not (OpCode.Call or OpCode.TailCall)) return MetaName(caller);
        if (ins.Op == OpCode.Call && pc + 1 < p.Code.Length && p.Code[pc + 1].Op == OpCode.TForLoop) return ("for iterator", "for iterator");
        string? kind = VarInfo.ObjName(p, pc, ins.A, out string name);
        return kind is null ? (null, null) : (kind, name);
    }

    // ---- getinfo ----

    private static LuaValue[] GetInfo(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        int a = 0;
        Coroutine co = state.Running;
        if (args.Length > 0 && args[0].Ref is Coroutine c)
        {
            co = c;
            a = 1;
        }
        string what = a + 1 < args.Length && !args[a + 1].IsNil ? LibArgs.CheckString(state, "getinfo", args, a + 1).ToString() : "flnSrtu";
        if (what.Contains('>')) throw LibArgs.ArgError("getinfo", a + 2, "invalid option '>'");
        foreach (char ch in what)
            if ("SlnrutLf".IndexOf(ch) < 0) throw LibArgs.ArgError("getinfo", a + 2, "invalid option");

        LuaValue fnv;
        List<Level>? levels = null;
        int idx = -1;
        if (a < args.Length && args[a].IsFunction) fnv = args[a];
        else
        {
            long level = LibArgs.CheckInteger("getinfo", args, a);
            levels = Levels(state, co, Lookup(state, "debug", "getinfo"), co == state.Running);
            if (level < 0 || level >= levels.Count) return [default];
            idx = (int)level;
            fnv = levels[idx].Fn;
        }
        var t = new LuaTable(0, 16);
        Proto? p = (fnv.Ref as LuaClosure)?.Proto;
        Frame? fr = idx >= 0 && levels![idx].Frame >= 0 ? co.Frames[levels[idx].Frame] : null;
        foreach (char ch in what)
        {
            switch (ch)
            {
                case 'S':
                    if (p is null)
                    {
                        Put(t, "source", Str("=[C]"));
                        Put(t, "short_src", Str("[C]"));
                        Put(t, "linedefined", LuaValue.FromInteger(-1));
                        Put(t, "lastlinedefined", LuaValue.FromInteger(-1));
                        Put(t, "what", Str("C"));
                    }
                    else
                    {
                        Put(t, "source", Str(p.ChunkName));
                        Put(t, "short_src", Str(ChunkId(p.ChunkName)));
                        Put(t, "linedefined", LuaValue.FromInteger(p.LineDefined));
                        Put(t, "lastlinedefined", LuaValue.FromInteger(p.LastLineDefined));
                        Put(t, "what", Str(p.LineDefined == 0 ? "main" : "Lua"));
                    }
                    break;
                case 'l':
                    Put(t, "currentline", LuaValue.FromInteger(fr is null ? -1 : CurrentLine(co, fr, levels![idx].Frame)));
                    break;
                case 'u':
                    Put(t, "nups", LuaValue.FromInteger(fnv.Ref is LuaClosure uc ? uc.Upvals.Length : (fnv.Ref as BuiltinFunction)?.NUpvalues ?? 0));
                    Put(t, "nparams", LuaValue.FromInteger(p?.NumParams ?? 0));
                    Put(t, "isvararg", LuaValue.FromBoolean(p?.IsVararg ?? true));
                    break;
                case 'n':
                {
                    (string? nw, string? nm) = idx >= 0 ? NameOf(co, levels!, idx) : (null, null);
                    Put(t, "name", nm is null ? default : Str(nm));
                    Put(t, "namewhat", Str(nw ?? ""));
                    break;
                }
                case 'r':
                {
                    bool hooked = idx >= 0 && idx == HookedLevel(co, levels!) && co.HookXferCount > 0;
                    Put(t, "ftransfer", LuaValue.FromInteger(hooked ? co.HookXferFirst : 0));
                    Put(t, "ntransfer", LuaValue.FromInteger(hooked ? co.HookXferCount : 0));
                    break;
                }
                case 't':
                    Put(t, "istailcall", LuaValue.FromBoolean(fr is { IsTail: true }));
                    break;
                case 'L':
                    if (p is not null)
                    {
                        var lines = new LuaTable();
                        foreach (int ln in p.Lines) lines.Set(LuaValue.FromInteger(ln), LuaValue.FromBoolean(true));
                        Put(t, "activelines", LuaValue.FromTable(lines));
                    }
                    break;
                case 'f':
                    Put(t, "func", fnv);
                    break;
            }
        }
        return [LuaValue.FromTable(t)];
    }

    private static void Put(LuaTable t, string key, LuaValue v) => t.Set(Str(key), v);

    // ---- traceback ----

    private static LuaValue[] Traceback(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        int a = 0;
        Coroutine co = state.Running;
        if (args.Length > 0 && args[0].Ref is Coroutine c)
        {
            co = c;
            a = 1;
        }
        LuaValue msg = a < args.Length ? args[a] : default;
        if (!msg.IsNil && !msg.IsString && !msg.IsNumber) return [msg];
        long level = LibArgs.OptInteger("traceback", args, a + 1, co == state.Running ? 1 : 0);
        var levels = Levels(state, co, Lookup(state, "debug", "traceback"), co == state.Running);
        return [Str(Format(state, co, levels, msg.IsNil ? null : msg.ToString(), (int)Math.Clamp(level, 0, int.MaxValue)))];
    }

    /// <summary>The stack traceback of <paramref name="co"/> as an uncaught error carries it (level 0 is the faulting Lua function).</summary>
    internal static string CaptureTraceback(LuaState state, Coroutine co) =>
        Format(state, co, Levels(state, co, default, false), null, 0);

    /// <summary>The structured form of <see cref="CaptureTraceback"/>: every level, innermost first, without the skipping of deep stacks.</summary>
    internal static LuaTraceEntry[] CaptureFrames(LuaState state, Coroutine co)
    {
        List<Level> lv = Levels(state, co, default, false);
        var frames = new LuaTraceEntry[lv.Count];
        for (int i = 0; i < frames.Length; i++)
        {
            Level l = lv[i];
            Proto? p = (l.Fn.Ref as LuaClosure)?.Proto;
            string? global = l.Fn.IsNil ? null : GlobalFuncName(state, l.Fn);
            (_, string? name) = NameOf(co, lv, i);
            frames[i] = p is null
                ? new LuaTraceEntry(global ?? name, "[C]", 0, true)
                : new LuaTraceEntry(global ?? name, ChunkId(p.ChunkName), CurrentLine(co, co.Frames[l.Frame]!, l.Frame), false);
        }
        return frames;
    }

    private static string Format(LuaState state, Coroutine co, List<Level> lv, string? msg, int level)
    {
        var sb = new StringBuilder();
        if (msg is not null) sb.Append(msg).Append('\n');
        sb.Append("stack traceback:");
        int last = lv.Count - 1;
        // The CLI driver (a Lua chunk named "=lunette") stands where lua.c's C code does: its frames end the traceback at "[C]: in ?".
        bool driverTail = false;
        {
            int o = last;
            bool sawDriver = false;
            for (; o >= 0; o--)
            {
                if (lv[o].Fn.Ref is LuaClosure { Proto.ChunkName: "=lunette" }) sawDriver = true;
                else if (lv[o].Fn.Ref is not BuiltinFunction) break;
            }
            if (sawDriver && o >= 0)
            {
                last = o;
                driverTail = true;
            }
        }
        int limit2show = last - level > Levels1 + Levels2 ? Levels1 : -1;
        while (level <= last)
        {
            int cur = level++;
            if (limit2show-- == 0)
            {
                int n = last - level - Levels2 + 1;
                sb.Append("\n\t...\t(skipping ").Append(n).Append(" levels)");
                level += n;
                continue;
            }
            Level l = lv[cur];
            Proto? p = (l.Fn.Ref as LuaClosure)?.Proto;
            if (p is null) sb.Append("\n\t[C]: in ");
            else
            {
                int line = CurrentLine(co, co.Frames[l.Frame]!, l.Frame);
                sb.Append("\n\t").Append(ChunkId(p.ChunkName)).Append(':').Append(line).Append(": in ");
            }
            string? global = l.Fn.IsNil ? null : GlobalFuncName(state, l.Fn);
            (string? what, string? name) = NameOf(co, lv, cur);
            if (global is not null) sb.Append("function '").Append(global).Append('\'');
            else if (what is not null && name is not null) sb.Append(what).Append(" '").Append(name).Append('\'');
            else if (p is not null && p.LineDefined == 0) sb.Append("main chunk");
            else if (p is not null) sb.Append("function <").Append(ChunkId(p.ChunkName)).Append(':').Append(p.LineDefined).Append('>');
            else sb.Append('?');
            if (l.Frame >= 0 && co.Frames[l.Frame]!.IsTail) sb.Append("\n\t(...tail calls...)");
        }
        if (driverTail) sb.Append("\n\t[C]: in ?");
        return sb.ToString();
    }

    /// <summary>"print" or "string.rep" when <paramref name="fn"/> is a global or a library function (lauxlib pushglobalfuncname).</summary>
    internal static string? GlobalFuncName(LuaState state, LuaValue fn)
    {
        LuaValue gkey = default;
        while (state.Globals.Next(gkey, out LuaValue gk, out LuaValue gv))
        {
            if (ReferenceEquals(gv.Ref, fn.Ref) && fn.Ref is not null && gk.Ref is byte[]) return gk.ToString();
            gkey = gk;
        }
        foreach (string lib in LibNames)
        {
            if (LibTable(state, lib) is not { } t) continue;
            LuaValue key = default;
            while (t.Next(key, out LuaValue k, out LuaValue v))
            {
                if (ReferenceEquals(v.Ref, fn.Ref) && k.Ref is byte[]) return lib + "." + k;
                key = k;
            }
        }
        return null;
    }

    // ---- locals ----

    /// <summary>The level of the function a running call or return hook reports (the one just below the hook function), or -1.</summary>
    private static int HookedLevel(Coroutine co, List<Level> levels)
    {
        if (co.AllowHook || co.HookFrameIndex < 0) return -1;
        for (int i = 0; i + 1 < levels.Count; i++)
            if (levels[i].Frame == co.HookFrameIndex) return i + 1;
        return -1;
    }

    /// <summary>Finds the n-th local (or vararg, for n &lt; 0, or temporary) of a Lua level; returns its name and stack slot.</summary>
    private static string? LocalAt(Coroutine co, Level l, int n, out int slot)
    {
        slot = -1;
        if (l.Frame < 0) return null;
        Frame fr = co.Frames[l.Frame]!;
        Proto p = fr.Closure.Proto;
        if (n < 0)
        {
            if (!p.IsVararg || -n > fr.VarargCount) return null;
            slot = fr.VarargBase + (-n - 1);
            return "(vararg)";
        }
        if (n == 0) return null;
        int pc = Math.Max(fr.Pc - 1, 0);
        int count = 0;
        foreach (LocVar lv in p.LocVars)
        {
            if (lv.StartPc <= pc && pc < lv.EndPc && ++count == n)
            {
                slot = fr.Base + lv.Reg;
                return lv.Name;
            }
        }
        int limit = fr.Base + p.MaxStack;
        if (pc < p.Code.Length && p.Code[pc].Op is OpCode.Call or OpCode.TailCall) limit = fr.Base + p.Code[pc].A;
        // A return hook may report values (an open vararg, say) that sit above the function's declared registers.
        if (co.HookXferCount > 0 && !co.AllowHook && l.Frame == co.HookFrameIndex - 1)
            limit = Math.Max(limit, fr.Base + co.HookXferFirst + co.HookXferCount - 1);
        if (limit - fr.Base >= n)
        {
            slot = fr.Base + n - 1;
            return "(temporary)";
        }
        return null;
    }

    private static LuaValue[] GetLocal(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        int a = 0;
        Coroutine co = state.Running;
        if (args.Length > 0 && args[0].Ref is Coroutine c)
        {
            co = c;
            a = 1;
        }
        int n = (int)LibArgs.CheckInteger("getlocal", args, a + 1);
        if (a < args.Length && args[a].IsFunction)
        {
            if (args[a].Ref is not LuaClosure cl) return [default];
            int count = 0;
            foreach (LocVar lv in cl.Proto.LocVars)
                if (lv.StartPc <= 0 && 0 < lv.EndPc && ++count == n) return [Str(lv.Name)];
            return [default];
        }
        long level = LibArgs.CheckInteger("getlocal", args, a);
        var levels = Levels(state, co, Lookup(state, "debug", "getlocal"), co == state.Running);
        if (level < 0 || level >= levels.Count) throw LibArgs.ArgError("getlocal", a + 1, "level out of range");
        if (level == HookedLevel(co, levels) && co.HookXferBase >= 0 && levels[(int)level].Frame < 0)
        {
            // The function a call or return hook reports is a host function: its values are the transferred stack slots.
            if (n < 1 || n > co.HookXferFirst + co.HookXferCount - 1) return [default];
            return [Str("(C temporary)"), co.Stack[co.HookXferBase + n - 1]];
        }
        if (level == 0 && co == state.Running)
        {
            // Level 0 is getlocal itself: its own arguments are the C function's stack slots.
            return n >= 1 && n <= args.Length ? [Str("(C temporary)"), args[n - 1]] : [default];
        }
        string? name = LocalAt(co, levels[(int)level], n, out int slot);
        if (name is null) return [default];
        return [Str(name), co.Stack[slot]];
    }

    private static LuaValue[] SetLocal(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        int a = 0;
        Coroutine co = state.Running;
        if (args.Length > 0 && args[0].Ref is Coroutine c)
        {
            co = c;
            a = 1;
        }
        long level = LibArgs.CheckInteger("setlocal", args, a);
        int n = (int)LibArgs.CheckInteger("setlocal", args, a + 1);
        LibArgs.CheckAny("setlocal", args, a + 2);
        var levels = Levels(state, co, Lookup(state, "debug", "setlocal"), co == state.Running);
        if (level < 0 || level >= levels.Count) throw LibArgs.ArgError("setlocal", a + 1, "level out of range");
        string? name = LocalAt(co, levels[(int)level], n, out int slot);
        if (name is null) return [default];
        co.Stack[slot] = args[a + 2];
        return [Str(name)];
    }

    // ---- upvalues ----

    private static LuaClosure? ClosureArg(ReadOnlySpan<LuaValue> args, int i, string fn)
    {
        if (i >= args.Length || !args[i].IsFunction) throw LibArgs.TypeError(fn, args, i, "function");
        return args[i].Ref as LuaClosure;
    }

    private static LuaValue[] GetUpvalue(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        int n = (int)LibArgs.CheckInteger("getupvalue", args, 1);
        LuaClosure? cl = ClosureArg(args, 0, "getupvalue");
        if (cl is null) return args[0].Ref is BuiltinFunction { NUpvalues: > 0 } bf && n >= 1 && n <= bf.NUpvalues ? [Str(""), default] : [];
        if (n < 1 || n > cl.Upvals.Length) return [];
        return [Str(UpvalName(cl, n)), cl.Upvals[n - 1].Get()];
    }

    private static string UpvalName(LuaClosure cl, int n) =>
        n - 1 < cl.Proto.UpvalNames.Length ? cl.Proto.UpvalNames[n - 1] : "(no name)";

    private static LuaValue[] SetUpvalue(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        int n = (int)LibArgs.CheckInteger("setupvalue", args, 1);
        LibArgs.CheckAny("setupvalue", args, 2);
        LuaClosure? cl = ClosureArg(args, 0, "setupvalue");
        if (cl is null || n < 1 || n > cl.Upvals.Length) return [];
        cl.Upvals[n - 1].Set(args[2]);
        return [Str(UpvalName(cl, n))];
    }

    private static LuaValue[] UpvalueId(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        int n = (int)LibArgs.CheckInteger("upvalueid", args, 1);
        LuaClosure? cl = ClosureArg(args, 0, "upvalueid");
        if (cl is null || n < 1 || n > cl.Upvals.Length) return [default];
        return [LuaValue.FromObject(cl.Upvals[n - 1])];
    }

    private static LuaValue[] UpvalueJoin(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        int n1 = (int)LibArgs.CheckInteger("upvaluejoin", args, 1);
        int n2 = (int)LibArgs.CheckInteger("upvaluejoin", args, 3);
        LuaClosure? c1 = ClosureArg(args, 0, "upvaluejoin");
        LuaClosure? c2 = ClosureArg(args, 2, "upvaluejoin");
        if (c1 is null) throw LibArgs.ArgError("upvaluejoin", 1, "Lua function expected");
        if (c2 is null) throw LibArgs.ArgError("upvaluejoin", 3, "Lua function expected");
        if (n1 < 1 || n1 > c1.Upvals.Length) throw LibArgs.ArgError("upvaluejoin", 2, "invalid upvalue index");
        if (n2 < 1 || n2 > c2.Upvals.Length) throw LibArgs.ArgError("upvaluejoin", 4, "invalid upvalue index");
        c1.Upvals[n1 - 1] = c2.Upvals[n2 - 1];
        return [];
    }

    // ---- hooks ----

    /// <summary>debug.sethook([thread,] hook, mask [, count]). The interpreter calls this directly (BuiltinKind.SetHook) so it can switch loops.</summary>
    /// <summary>debug.getuservalue(u, n): a host userdata has exactly one user value (lua_newuserdata), so only n == 1 exists.</summary>
    private static LuaValue[] GetUserValue(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        long n = LibArgs.OptInteger("getuservalue", args, 1, 1);
        if (args.Length == 0 || args[0].Ref is not LuaUserdata u || u.Value is LuaFile) return [default]; // files carry no user values
        return n == 1 ? [u.UserValue, LuaValue.FromBoolean(true)] : [default];
    }

    private static LuaValue[] SetUserValue(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        if (args.Length == 0 || args[0].Ref is not LuaUserdata u) throw LibArgs.TypeError("setuservalue", args, 0, "userdata");
        LibArgs.CheckAny("setuservalue", args, 1);
        long n = LibArgs.OptInteger("setuservalue", args, 2, 1);
        if (n != 1 || u.Value is LuaFile) return [default];
        u.UserValue = args[1];
        return [args[0]];
    }

    internal static LuaValue[]? SetHook(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        int a = 0;
        Coroutine co = state.Running;
        if (args.Length > 0 && args[0].Ref is Coroutine c)
        {
            co = c;
            a = 1;
        }
        if (a >= args.Length || args[a].IsNil)
        {
            ClearHook(co);
            return null;
        }
        string smask = LibArgs.CheckString(state, "sethook", args, a + 1).ToString();
        if (!args[a].IsFunction) throw LibArgs.TypeError("sethook", args, a, "function");
        long count = LibArgs.OptInteger("sethook", args, a + 2, 0);
        byte mask = 0;
        if (smask.Contains('c')) mask |= Coroutine.HookCall;
        if (smask.Contains('r')) mask |= Coroutine.HookReturn;
        if (smask.Contains('l')) mask |= Coroutine.HookLine;
        if (count > 0) mask |= Coroutine.HookCount;
        if (mask == 0)
        {
            ClearHook(co);
            return null;
        }
        co.HookFn = args[a];
        co.HookMask = mask;
        co.HookPeriod = co.HookCounter = (int)Math.Clamp(count, 0, int.MaxValue);
        return null;
    }

    private static void ClearHook(Coroutine co)
    {
        co.HookFn = default;
        co.HookMask = 0;
        co.HookPeriod = co.HookCounter = 0;
    }

    private static LuaValue[] GetHook(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        Coroutine co = args.Length > 0 && args[0].Ref is Coroutine c ? c : state.Running;
        if (co.HookMask == 0) return [default];
        string mask = ((co.HookMask & Coroutine.HookCall) != 0 ? "c" : "") + ((co.HookMask & Coroutine.HookReturn) != 0 ? "r" : "")
            + ((co.HookMask & Coroutine.HookLine) != 0 ? "l" : "");
        return [co.HookFn, Str(mask), LuaValue.FromInteger(co.HookPeriod)];
    }

    // ---- metatables ----

    private static LuaValue[] GetMetatable(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LibArgs.CheckAny("getmetatable", args, 0);
        LuaTable? mt = Meta.GetMetatable(state, args[0]);
        return [mt is null ? default : LuaValue.FromTable(mt)];
    }

    private static LuaValue[] SetMetatable(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LibArgs.CheckAny("setmetatable", args, 0);
        if (args.Length < 2 || !(args[1].IsNil || args[1].Ref is LuaTable)) throw LibArgs.ArgError("setmetatable", 2, "nil or table expected");
        var mt = args[1].Ref as LuaTable;
        LuaValue v = args[0];
        if (v.Ref is LuaTable t)
        {
            t.Metatable = mt;
            state.Gc.OnSetMetatable(t, mt);
        }
        else if (v.Ref is LuaUserdata ud)
        {
            ud.Metatable = mt;
            state.Gc.OnSetMetatable(ud, mt);
        }
        else if (v.IsString) state.StringMetatable = mt;
        else state.SetTypeMetatable(v, mt);
        return [v];
    }

    // ---- debug.debug ----

    private static LuaValue[] DebugPrompt(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        while (true)
        {
            Console.Error.Write("lua_debug> ");
            string? line = Console.In.ReadLine();
            if (line is null || line == "cont") return [];
            try
            {
                Proto proto = LuaCompiler.Compile(line, "=(debug command)", state.Strings);
                Interpreter.CallValues(state, LuaValue.FromObject(state.NewMainClosure(proto, LuaValue.FromTable(state.Globals))), []);
            }
            catch (LuaException e)
            {
                Console.Error.WriteLine(e.Message);
            }
        }
    }
}
