namespace Lunette;

// Dev-mode console support: evaluate a snippet inside a live state and get values or a structured error back.
public sealed partial class LuaState
{
    /// <summary>
    /// Compiles and runs <paramref name="snippet"/> in this state, with its globals and registered modules (<c>require</c>) and under
    /// its budgets, as a developer console would. Like the standalone REPL it first tries the snippet as an expression
    /// (<c>return snippet</c>), then as statements. A compile or runtime error is returned in <see cref="LuaEvalResult.Error"/>
    /// with chunk, line and <see cref="LuaException.TraceEntries"/> rather than thrown; the state stays usable.
    /// A budget or memory abort still throws <see cref="LuaAbortException"/> and ends the state (<see cref="IsAborted"/>).
    /// </summary>
    public LuaEvalResult Evaluate(string snippet, string chunkName = "=(console)")
    {
        ArgumentNullException.ThrowIfNull(snippet);
        ArgumentNullException.ThrowIfNull(chunkName);
        LuaFunction fn;
        try
        {
            try
            {
                fn = Load("return " + snippet, chunkName);
            }
            catch (LuaException)
            {
                fn = Load(snippet, chunkName);
            }
            return new LuaEvalResult(Call(fn, ReadOnlySpan<LuaValue>.Empty), null);
        }
        catch (LuaException e)
        {
            return new LuaEvalResult([], e);
        }
    }
}
