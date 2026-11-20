using System.Text;
using Lunette.Lib;
using Lunette.Vm;

namespace Lunette;

/// <summary>
/// Chunk loading and modules: load, loadfile, dofile, require, the <c>package</c> table, and warn.
/// Text chunks only; precompiled binary chunks are out of scope.
/// </summary>
internal static class LoadLib
{
    private const string DefaultPath = "./?.lua;./?/init.lua";
    private static readonly byte[] BinarySignature = [0x1B, (byte)'L', (byte)'u', (byte)'a'];

    public static void Register(LuaState state)
    {
        state.SetGlobal("load", LuaValue.FromObject(new BuiltinFunction(Load)));
        state.SetGlobal("loadfile", LuaValue.FromObject(new BuiltinFunction(LoadFile)));
        state.SetGlobal("dofile", LuaValue.FromObject(new BuiltinFunction(DoFile)));
        state.SetGlobal("require", LuaValue.FromObject(new BuiltinFunction(Require)));
        state.SetGlobal("warn", LuaValue.FromObject(new BuiltinFunction(Warn)));

        var package = new LuaTable(0, 8);
        var loaded = new LuaTable(0, 16);
        var preload = new LuaTable();
        var searchers = new LuaTable(4, 0);
        searchers.SetInt(1, LuaValue.FromObject(new BuiltinFunction(SearcherPreload)));
        searchers.SetInt(2, LuaValue.FromObject(new BuiltinFunction(SearcherLua)));
        Put(state, package, "loaded", LuaValue.FromTable(loaded));
        Put(state, package, "preload", LuaValue.FromTable(preload));
        Put(state, package, "searchers", LuaValue.FromTable(searchers));
        Put(state, package, "path", LuaValue.FromString(EnvPath("LUA_PATH", DefaultPath)));
        Put(state, package, "cpath", LuaValue.FromString(EnvPath("LUA_CPATH", "")));
        Put(state, package, "config", LuaValue.FromString("/\n;\n?\n!\n-\n"));
        Put(state, package, "searchpath", LuaValue.FromObject(new BuiltinFunction(SearchPath)));
        Put(state, package, "loadlib", LuaValue.FromObject(new BuiltinFunction(static (_, _) =>
            [default, LuaValue.FromString("dynamic libraries not enabled; check your Lua installation"), LuaValue.FromString("absent")])));
        state.Package = package;
        state.SetGlobal("package", LuaValue.FromTable(package));
        foreach (string lib in new[] { "_G", "package", "coroutine", "table", "string", "math", "utf8", "io", "os", "debug" })
        {
            LuaValue v = state.Globals.Get(LuaValue.FromString(state.Strings.FromUtf16(lib)));
            if (!v.IsNil) Put(state, loaded, lib, v);
        }
    }

    private static string EnvPath(string name, string dflt)
    {
        string? v = Environment.GetEnvironmentVariable(name + "_5_4") ?? Environment.GetEnvironmentVariable(name);
        if (v is null) return dflt;
        int i = v.IndexOf(";;", StringComparison.Ordinal);
        if (i < 0) return v;
        string head = v[..i], tail = v[(i + 2)..];
        return (head.Length > 0 ? head + ";" : "") + dflt + (tail.Length > 0 ? ";" + tail : "");
    }

    private static void Put(LuaState state, LuaTable t, string key, LuaValue v) =>
        t.Set(LuaValue.FromString(state.Strings.FromUtf16(key)), v);

    private static LuaValue Str(LuaState state, string s) => LuaValue.FromString(state.Strings.FromUtf16(s));

    private static LuaValue Arg(ReadOnlySpan<LuaValue> args, int i) => i < args.Length ? args[i] : default;

    // ---- load / loadfile / dofile ----

    /// <summary>Compiles <paramref name="source"/> after checking the mode; a failure is returned as a message, not thrown.</summary>
    private static LuaValue[] Compile(LuaState state, byte[] source, string chunkName, string mode, LuaValue env)
    {
        bool binary = source.AsSpan().StartsWith(BinarySignature);
        if (binary)
        {
            if (!mode.Contains('b')) return [default, Str(state, $"attempt to load a binary chunk (mode is '{mode}')")];
            return [default, Str(state, $"{ChunkIdOf(chunkName)}: precompiled chunks are not supported")];
        }
        if (!mode.Contains('t')) return [default, Str(state, $"attempt to load a text chunk (mode is '{mode}')")];
        try
        {
            return [LuaValue.FromObject(state.Load(source, chunkName, env))];
        }
        catch (LuaException e)
        {
            return [default, e.Value];
        }
    }

    private static string ChunkIdOf(string name) => Compiler.ChunkId.Display(name);

    private static string Mode(LuaState state, ReadOnlySpan<LuaValue> args, int i, string fn) =>
        i < args.Length && !args[i].IsNil ? LibArgs.CheckString(state, fn, args, i).ToString() : "bt";

    private static LuaValue[] Load(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaValue chunk = Arg(args, 0);
        string mode = Mode(state, args, 2, "load");
        LuaValue env = args.Length > 3 ? args[3] : LuaValue.FromTable(state.Globals);
        if (chunk.Ref is byte[] raw)
        {
            var s = new LuaString(raw);
            string name = args.Length > 1 && !args[1].IsNil ? LibArgs.CheckString(state, "load", args, 1).ToString() : s.ToString();
            return Compile(state, s.Bytes.ToArray(), name, mode, env);
        }
        if (!chunk.IsFunction) throw LuaErrors.TypeError(args, 0, "load", "function");
        string rname = args.Length > 1 && !args[1].IsNil ? LibArgs.CheckString(state, "load", args, 1).ToString() : "=(load)";
        var buf = new List<byte>();
        while (true)
        {
            LuaValue[] piece = Interpreter.CallValues(state, chunk, []);
            LuaValue p = piece.Length > 0 ? piece[0] : default;
            if (p.IsNil) break;
            if (p.Ref is not byte[] pb) return [default, Str(state, "reader function must return a string")];
            var ps = new LuaString(pb);
            if (ps.Length == 0) break;
            buf.AddRange(ps.Bytes.ToArray());
        }
        return Compile(state, buf.ToArray(), rname, mode, env);
    }

    /// <summary>Reads a source file, dropping a UTF-8 BOM and a leading '#' line (the newline stays so line numbers hold).</summary>
    private static byte[]? ReadSource(string path, out string error)
    {
        error = "";
        byte[] data;
        try
        {
            data = path.Length == 0 ? ReadStdin() : File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error = $"cannot open {path}: {(e is FileNotFoundException or DirectoryNotFoundException ? "No such file or directory" : e.Message)}";
            return null;
        }
        int start = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) start = 3;
        if (start < data.Length && data[start] == (byte)'#')
        {
            while (start < data.Length && data[start] != (byte)'\n') start++;
        }
        return start == 0 ? data : data[start..];
    }

    private static byte[] ReadStdin()
    {
        using var ms = new MemoryStream();
        Console.OpenStandardInput().CopyTo(ms);
        return ms.ToArray();
    }

    private static LuaValue[] LoadFileCore(LuaState state, string? filename, string mode, LuaValue env)
    {
        byte[]? src = ReadSource(filename ?? "", out string error);
        if (src is null) return [default, Str(state, error)];
        return Compile(state, src, filename is null ? "=stdin" : "@" + filename, mode, env);
    }

    private static LuaValue[] LoadFile(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        string? filename = args.Length > 0 && !args[0].IsNil ? LibArgs.CheckString(state, "loadfile", args, 0).ToString() : null;
        string mode = Mode(state, args, 1, "loadfile");
        LuaValue env = args.Length > 2 ? args[2] : LuaValue.FromTable(state.Globals);
        return LoadFileCore(state, filename, mode, env);
    }

    private static LuaValue[] DoFile(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        string? filename = args.Length > 0 && !args[0].IsNil ? LibArgs.CheckString(state, "dofile", args, 0).ToString() : null;
        LuaValue[] loaded = LoadFileCore(state, filename, "bt", LuaValue.FromTable(state.Globals));
        if (loaded[0].IsNil) throw new LuaException(loaded[1]);
        return Interpreter.CallValues(state, loaded[0], []);
    }

    // ---- warn ----

    private static LuaValue[]? Warn(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LibArgs.CheckString(state, "warn", args, 0);
        for (int i = 1; i < args.Length; i++) LibArgs.CheckString(state, "warn", args, i);
        if (args.Length == 1 && new LuaString((byte[])args[0].Ref!) is { Length: > 0 } ctl && ctl[0] == (byte)'@')
        {
            string c = ctl.ToString();
            if (c == "@on") state.WarningsOn = true;
            else if (c == "@off") state.WarningsOn = false;
            return null;
        }
        if (!state.WarningsOn) return null;
        var sb = new StringBuilder("Lua warning: ");
        for (int i = 0; i < args.Length; i++) sb.Append(LibArgs.CheckString(state, "warn", args, i).ToString());
        state.Warn?.Invoke(sb.ToString());
        if (state.Warn is null) Console.Error.WriteLine(sb.ToString());
        return null;
    }

    // ---- package ----

    private static string PackageString(LuaState state, string field)
    {
        LuaValue v = state.Package!.Get(Str(state, field));
        if (v.Ref is byte[] s) return new LuaString(s).ToString();
        throw LuaErrors.Error($"'package.{field}' must be a string");
    }

    /// <summary>Returns the first readable file of <paramref name="path"/> for <paramref name="name"/>, else null with the "no file" list.</summary>
    private static string? SearchPathCore(string name, string path, string sep, string rep, out string error)
    {
        if (sep.Length > 0) name = name.Replace(sep, rep);
        var tried = new StringBuilder();
        foreach (string template in path.Split(';'))
        {
            if (template.Length == 0) continue;
            string file = template.Replace("?", name);
            if (File.Exists(file)) { error = ""; return file; }
            if (tried.Length > 0) tried.Append("\n\t");
            tried.Append("no file '").Append(file).Append('\'');
        }
        error = tried.ToString();
        return null;
    }

    private static LuaValue[] SearchPath(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        string name = LibArgs.CheckString(state, "searchpath", args, 0).ToString();
        string path = LibArgs.CheckString(state, "searchpath", args, 1).ToString();
        string sep = args.Length > 2 && !args[2].IsNil ? LibArgs.CheckString(state, "searchpath", args, 2).ToString() : ".";
        string rep = args.Length > 3 && !args[3].IsNil ? LibArgs.CheckString(state, "searchpath", args, 3).ToString() : "/";
        string? file = SearchPathCore(name, path, sep, rep, out string error);
        return file is null ? [default, Str(state, error)] : [Str(state, file)];
    }

    private static LuaValue[] SearcherPreload(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaValue name = args[0];
        LuaValue preload = state.Package!.Get(Str(state, "preload"));
        if (preload.Ref is not LuaTable pt) throw LuaErrors.Error("'package.preload' must be a table");
        LuaValue loader = pt.Get(name);
        if (loader.IsNil) return [Str(state, $"no field package.preload['{new LuaString((byte[])name.Ref!)}']")];
        return [loader, Str(state, ":preload:")];
    }

    private static LuaValue[] SearcherLua(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        string name = new LuaString((byte[])args[0].Ref!).ToString();
        string? file = SearchPathCore(name, PackageString(state, "path"), ".", "/", out string error);
        if (file is null) return [Str(state, error)];
        LuaValue[] r = LoadFileCore(state, file, "bt", LuaValue.FromTable(state.Globals));
        if (r[0].IsNil)
            throw LuaErrors.Error($"error loading module '{name}' from file '{file}':\n\t{Meta.ToStringValue(state, r[1])}");
        return [r[0], Str(state, file)];
    }

    private static LuaValue[] Require(LuaState state, ReadOnlySpan<LuaValue> args)
    {
        LuaString nameStr = LibArgs.CheckString(state, "require", args, 0);
        LuaValue name = LuaValue.FromString(nameStr);
        if (state.Package!.Get(Str(state, "loaded")).Ref is not LuaTable loaded)
            throw LuaErrors.Error("'package.loaded' must be a table");
        LuaValue cached = loaded.Get(name);
        if (!cached.IsFalsy) return [cached];

        if (state.Package.Get(Str(state, "searchers")).Ref is not LuaTable searchers)
            throw LuaErrors.Error("'package.searchers' must be a table");
        var msg = new StringBuilder();
        LuaValue loader = default, extra = default;
        for (long i = 1; ; i++)
        {
            msg.Append("\n\t");
            LuaValue searcher = searchers.GetInt(i);
            if (searcher.IsNil)
            {
                msg.Length -= 2;
                throw LuaErrors.Error($"module '{nameStr}' not found:{msg}");
            }
            LuaValue[] r = Interpreter.CallValues(state, searcher, [name]);
            LuaValue first = r.Length > 0 ? r[0] : default;
            if (first.IsFunction)
            {
                loader = first;
                extra = r.Length > 1 ? r[1] : default;
                break;
            }
            if (first.IsString || first.IsNumber) msg.Append(Meta.ToStringValue(state, first).ToString());
            else msg.Length -= 2;
        }
        LuaValue[] res = Interpreter.CallValues(state, loader, [name, extra]);
        if (res.Length > 0 && !res[0].IsNil) loaded.Set(name, res[0]);
        else if (loaded.Get(name).IsNil) loaded.Set(name, LuaValue.FromBoolean(true));
        return [loaded.Get(name), extra];
    }
}
