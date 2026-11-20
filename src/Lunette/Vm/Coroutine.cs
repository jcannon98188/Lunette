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

    public void Reset()
    {
        Depth = 0;
        InHandler = false;
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
