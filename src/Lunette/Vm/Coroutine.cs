using Lunette.Compiler;

namespace Lunette.Vm;

/// <summary>A captured variable: open while it still lives in a Coroutine's stack, closed once it owns its value.</summary>
internal sealed class UpVal
{
    /// <summary>The Coroutine whose stack holds the variable while open; null once closed.</summary>
    public Coroutine? Owner;
    public int Index;
    public LuaValue Value;

    public LuaValue Get() => Owner is null ? Value : Owner.Stack[Index];

    public void Set(LuaValue v)
    {
        if (Owner is null) Value = v;
        else Owner.Stack[Index] = v;
    }
}

/// <summary>One heap-allocated activation record for a Lua call (ADR 0002).</summary>
internal sealed class Frame
{
    public LuaClosure Closure = null!;
    public int Base;
    public int Pc;
    /// <summary>Stack index of the called function; results are written starting here.</summary>
    public int FuncIdx;
    /// <summary>Number of results the caller wants, or -1 for all.</summary>
    public int Wanted;
    public int VarargBase;
    public int VarargCount;
    /// <summary>Non-zero when this Frame is the target of a pcall (1) or xpcall (2): errors below it stop here.</summary>
    public byte PcallKind;
    /// <summary>Stack index where a protected call puts its status (true/false); its results follow.</summary>
    public int PcallBase;
    /// <summary>The xpcall message handler.</summary>
    public LuaValue Handler;
    /// <summary>Non-zero when this Frame runs a metamethod on behalf of an instruction of the Frame below: its first result finishes that instruction.</summary>
    public Continuation Cont;
    /// <summary>Stack index the finished instruction stores its result into.</summary>
    public int ContDest;
    /// <summary>True when this Frame replaced its caller through a tail call (debug.getinfo istailcall).</summary>
    public bool IsTail;
    /// <summary>True when a host function (not a Lua call instruction) called this Frame, so no call-site name exists.</summary>
    public bool Native;
}

/// <summary>How a metamethod Frame finishes the instruction that called it (the equivalent of luaV_finishOp).</summary>
internal enum Continuation : byte
{
    None,
    /// <summary>Store the first result.</summary>
    Value,
    /// <summary>Store the truthiness of the first result as a boolean.</summary>
    Bool,
    /// <summary>Drop the results.</summary>
    Discard,
}

internal enum CoStatus : byte
{
    Suspended,
    Running,
    Normal,
    Dead,
}

/// <summary>A growable value stack plus its Frames and open upvalues.</summary>
internal sealed class Coroutine
{
    /// <summary>Largest stack, in slots, matching PUC's LUAI_MAXSTACK.</summary>
    public const int StackLimit = 1_000_000;

    /// <summary>Extra slots granted to a message handler running after a stack overflow (PUC's ERRORSTACKSIZE).</summary>
    public const int HandlerHeadroom = 200;

    /// <summary>True while an xpcall message handler runs: the stack may exceed <see cref="StackLimit"/> by <see cref="HandlerHeadroom"/>.</summary>
    public bool InHandler;

    public bool IsMain;
    public CoStatus Status = CoStatus.Suspended;
    /// <summary>The body function, until the first resume starts it.</summary>
    public LuaValue Body;
    public bool Started;
    /// <summary>Set by a yield: the dispatch loop returned so the resumer can take <see cref="YieldValues"/>.</summary>
    public bool Yielded;
    /// <summary>A full collection requested by <c>collectgarbage</c>; the loop returned so <see cref="Interpreter"/> runs it, and where its result goes.</summary>
    public bool CollectPending;
    public int CollectFa, CollectNargs, CollectWanted;
    public LuaValue[] YieldValues = [];
    /// <summary>Where the results of the pending yield call go once resumed, how many are wanted, and whether a pcall status (true) leads them.</summary>
    public int YieldFa;
    public int YieldWanted;
    /// <summary>The continuation of the host function that yielded, run with the resume values; null when the values are the results.</summary>
    public HostContinuation? YieldCont;
    public bool YieldPcall;
    /// <summary>Number of non-yieldable host calls in progress on this coroutine (PUC nny).</summary>
    public int Nny;
    /// <summary>The error that killed the coroutine, reported once by coroutine.close.</summary>
    public LuaValue? ErrorValue;

    // ---- debug hooks (debug.sethook), per coroutine ----
    public const byte HookCall = 1, HookReturn = 2, HookLine = 4, HookCount = 8;
    /// <summary>The hook function, or nil.</summary>
    public LuaValue HookFn;
    /// <summary>Which events call the hook (HookCall | HookReturn | HookLine | HookCount); 0 means the fast loop runs.</summary>
    public byte HookMask;
    /// <summary>The count-hook period and the instructions left before it fires.</summary>
    public int HookPeriod, HookCounter;
    /// <summary>False while the hook function itself runs, so hooks do not nest.</summary>
    public bool AllowHook = true;
    /// <summary>Previous instruction index seen by the line hook.</summary>
    public int OldPc;
    /// <summary>Set by sethook when the running coroutine must switch between the fast and the traced loop.</summary>
    public bool Reenter;
    /// <summary>Set by sethook: the traced loop owes a return event for the sethook call that installed it.</summary>
    public bool PendingReturnHook;
    /// <summary>Frames index of the running hook function, or -1.</summary>
    public int HookFrameIndex = -1;
    /// <summary>The host function whose call or return event is being reported, or nil.</summary>
    public LuaValue HookBuiltin;

    /// <summary>The error or assert builtin that is raising the error being unwound, so a handler's traceback lists it.</summary>
    public LuaValue RaisingBuiltin;

    /// <summary>The source line of the instruction that killed this coroutine's top Frame, and the Frame depth it belongs to (the saved pc is stale for VM-detected errors).</summary>
    public int FaultLine, FaultDepth;

    /// <summary>True when the traced loop must run: a hook is set and not currently inside the hook function.</summary>
    public bool Traced => HookMask != 0 && AllowHook;

    public override string ToString() => $"thread: 0x{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this):x8}";

    public LuaValue[] Stack = new LuaValue[256];
    public Frame?[] Frames = new Frame?[16];
    public int Depth;
    /// <summary>Where the dispatch loop resumes after a handled error or a recognised builtin: the Frame...</summary>
    public Frame? ResumeFrame;
    /// <summary>...and the value of its <c>top</c> (the end of a variable result list).</summary>
    public int ResumeTop;
    /// <summary>First stack index free for calls made on behalf of metamethods and builtins.</summary>
    public int CallTop;
    /// <summary>Open upvalues, ascending by stack index.</summary>
    public readonly List<UpVal> OpenUpvals = new();
    /// <summary>Stack indices of live to-be-closed variables, ascending.</summary>
    public readonly List<int> Tbc = new();

    public void Reset()
    {
        Tbc.Clear();
        Depth = 0;
        InHandler = false;
        Nny = 0;
        OpenUpvals.Clear();
    }

    public void EnsureStack(int size, Proto? where, int pc)
    {
        if (size <= Stack.Length) return;
        if (size > StackLimit + (InHandler ? HandlerHeadroom : 0))
        {
            if (InHandler) throw new LuaException("error in error handling");
            string prefix = where is null ? "" : Interpreter.Where(where, pc) + " ";
            throw new LuaException(prefix + "stack overflow");
        }
        Array.Resize(ref Stack, Math.Max(size, Math.Min(Stack.Length * 2, InHandler ? StackLimit + HandlerHeadroom : StackLimit)));
    }

    public Frame PushFrame()
    {
        if (Depth == Frames.Length) Array.Resize(ref Frames, Frames.Length * 2);
        Frame f = Frames[Depth++] ??= new Frame();
        f.PcallKind = 0;
        f.Cont = Continuation.None;
        f.IsTail = false;
        f.Native = false;
        return f;
    }

    public UpVal FindUpval(int index)
    {
        int pos = OpenUpvals.Count;
        while (pos > 0 && OpenUpvals[pos - 1].Index >= index)
        {
            if (OpenUpvals[pos - 1].Index == index) return OpenUpvals[pos - 1];
            pos--;
        }
        var uv = new UpVal { Owner = this, Index = index };
        OpenUpvals.Insert(pos, uv);
        return uv;
    }

    /// <summary>Closes every open upvalue at or above stack index <paramref name="level"/>.</summary>
    public void Close(int level)
    {
        int n = OpenUpvals.Count;
        while (n > 0 && OpenUpvals[n - 1].Index >= level)
        {
            var uv = OpenUpvals[n - 1];
            uv.Value = Stack[uv.Index];
            uv.Owner = null;
            n--;
        }
        OpenUpvals.RemoveRange(n, OpenUpvals.Count - n);
    }
}
