namespace Lunette.Lib;

/// <summary><c>collectgarbage</c>, mapped best-effort onto the .NET GC (ticket 12).</summary>
internal static class GcLib
{
    public static void Register(LuaState state) =>
        state.Globals["collectgarbage"] = LuaValue.FromObject(new BuiltinFunction(CollectGarbage, BuiltinKind.Collect));

    private static LuaValue[]? CollectGarbage(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        string option = args.Length == 0 || args[0].IsNil ? "collect" : LibArgs.CheckString(state, "collectgarbage", args, 0).ToString();
        var gc = state.Gc;
        // The collector is not reentrant: inside a finalizer every option answers with fail (nil).
        if (option is not ("collect" or "stop" or "restart" or "count" or "step" or "isrunning"
                           or "incremental" or "generational" or "setpause" or "setstepmul"))
            throw LuaErrors.ArgError(1, "collectgarbage", $"invalid option '{option}'");
        if (gc.InCollector) return [default];

        switch (option)
        {
            case "collect":
                gc.FullCollect();
                return [LuaValue.FromInteger(0)];
            case "stop":
                gc.Running = false;
                return [LuaValue.FromInteger(0)];
            case "restart":
                gc.Running = true;
                return [LuaValue.FromInteger(0)];
            case "count":
                return [LuaValue.FromFloat(GC.GetTotalMemory(false) / 1024.0)];
            case "step":
                gc.Step();
                return [LuaValue.FromBoolean(true)];
            case "isrunning":
                return [LuaValue.FromBoolean(gc.Running)];
            case "incremental":
            case "generational":
            {
                string previous = gc.Mode;
                gc.Mode = option;
                if (option == "incremental")
                {
                    long pause = LibArgs.OptInteger("collectgarbage", args, 1, 0);
                    long mul = LibArgs.OptInteger("collectgarbage", args, 2, 0);
                    if (pause != 0) gc.Pause = (int)Math.Clamp(pause, 0, int.MaxValue);
                    if (mul != 0) gc.StepMul = (int)Math.Clamp(mul, 0, int.MaxValue);
                }
                return [LuaValue.FromString(state.Strings.FromUtf16(previous))];
            }
            case "setpause":
            {
                int previous = gc.Pause;
                gc.Pause = (int)Math.Clamp(LibArgs.OptInteger("collectgarbage", args, 1, 0), 0, int.MaxValue);
                return [LuaValue.FromInteger(previous)];
            }
            default: // setstepmul
            {
                int previous = gc.StepMul;
                gc.StepMul = (int)Math.Clamp(LibArgs.OptInteger("collectgarbage", args, 1, 0), 0, int.MaxValue);
                return [LuaValue.FromInteger(previous)];
            }
        }
    }
}
