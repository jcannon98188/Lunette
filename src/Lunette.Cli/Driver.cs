using System.Text;

namespace Lunette.Cli;

/// <summary>
/// The standalone interpreter's main loop (lua.c's <c>pmain</c>, <c>doREPL</c> and friends), written in Lua so it runs on the
/// public surface of the engine. <see cref="Header"/> turns the parsed command line into the constants the body reads.
/// </summary>
internal static class Driver
{
    public const string Version = "Lunette (Lua 5.4 compatible)  Copyright (C) 1994-2025 Lua.org, PUC-Rio";

    public sealed record Options(
        string ProgName,
        bool HasI,
        bool HasV,
        bool HasE,
        bool NoEnv,
        bool StdinTty,
        IReadOnlyList<(char Kind, string Value)> Actions,
        string? Script,
        bool ScriptIsStdin);

    /// <summary>A Lua string literal for <paramref name="s"/>: ASCII only, every other byte as a decimal escape.</summary>
    internal static string Quote(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (byte b in Encoding.UTF8.GetBytes(s))
        {
            if (b == '"' || b == '\\') sb.Append('\\').Append((char)b);
            else if (b is >= 32 and < 127) sb.Append((char)b);
            else sb.Append('\\').Append(b.ToString("D3"));
        }
        return sb.Append('"').ToString();
    }

    private static string Bool(bool b) => b ? "true" : "false";

    public static byte[] Build(Options o)
    {
        var sb = new StringBuilder();
        sb.Append("local PROGNAME, VERSION = ").Append(Quote(o.ProgName)).Append(", ").Append(Quote(Version)).Append('\n');
        sb.Append("local HAS_I, HAS_V, HAS_E, NOENV, STDIN_TTY = ")
          .Append(Bool(o.HasI)).Append(", ").Append(Bool(o.HasV)).Append(", ").Append(Bool(o.HasE)).Append(", ")
          .Append(Bool(o.NoEnv)).Append(", ").Append(Bool(o.StdinTty)).Append('\n');
        sb.Append("local ACTIONS = {");
        foreach ((char kind, string value) in o.Actions) sb.Append('{').Append(Quote(kind.ToString())).Append(',').Append(Quote(value)).Append("},");
        sb.Append("}\n");
        sb.Append("local SCRIPT = ").Append(o.Script is null ? "nil" : o.ScriptIsStdin ? "false" : Quote(o.Script)).Append('\n');
        sb.Append(Body);
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private const string Body = """
local stdout, stderr = io.stdout, io.stderr

local function message(msg)
  stderr:write(PROGNAME, ": ", msg, "\n")
end

local function msghandler(msg)
  if type(msg) ~= "string" and type(msg) ~= "number" then
    local mt = getmetatable(msg)
    local ts = mt and mt.__tostring
    if ts then
      local s = ts(msg)
      if type(s) == "string" then return s end
    end
    msg = string.format("(error object is a %s value)", type(msg))
  end
  local tb = debug.traceback(tostring(msg), 2)
  -- Hide the driver's own frames (docall and below) from the traceback.
  local mark, at = "\n\t[C]: in function 'xpcall'\n\tlunette:", nil
  local from = 1
  while true do
    local s = string.find(tb, mark, from, true)
    if not s then break end
    at, from = s, s + 1
  end
  return at and string.sub(tb, 1, at - 1) or tb
end

local function report(ok, msg)
  if not ok then message(msg == nil and "(no error message)" or tostring(msg)) end
  return ok
end

local function docall(f, ...)
  return xpcall(f, msghandler, ...)
end

local function dostring(s, name)
  local f, err = load(s, name)
  if not f then message(err); return false end
  return report(docall(f))
end

local function dofile_(name, ...)
  local f, err = loadfile(name)
  if not f then message(err); return false end
  return report(docall(f, ...))
end

local function dolibrary(name)
  local glob, mod = name:match("^([^=]*)=(.*)$")
  local suffix
  if not glob then
    mod = name
    glob = name
    suffix = name:find("-", 1, true)
  end
  local ok, res = docall(require, mod)
  if ok then
    if suffix then glob = glob:sub(1, suffix - 1) end
    _G[glob] = res
  end
  return report(ok, res)
end

local function setpath(field, envname)
  local v = os.getenv(envname .. "_5_4") or os.getenv(envname)
  if not v then return end
  local dflt = package[field]
  local i = v:find(";;", 1, true)
  if i then
    local head, tail = v:sub(1, i - 1), v:sub(i + 2)
    v = (head ~= "" and head .. ";" or "") .. dflt .. (tail ~= "" and ";" .. tail or "")
  end
  package[field] = v
end

local function handle_init()
  local name = "=LUA_INIT_5_4"
  local init = os.getenv("LUA_INIT_5_4")
  if init == nil then
    name = "=LUA_INIT"
    init = os.getenv("LUA_INIT")
  end
  if init == nil then return true end
  if init:sub(1, 1) == "@" then return dofile_(init:sub(2)) end
  return dostring(init, name)
end

local function pushargs()
  if type(arg) ~= "table" then error("'arg' is not a table", 0) end
  return table.unpack(arg, 1, #arg)
end

local function repl()
  local function getprompt(first)
    local p = _G[first and "_PROMPT" or "_PROMPT2"]
    if p == nil then return first and "> " or ">> " end
    return tostring(p)
  end
  local function pushline(first)
    stdout:write(getprompt(first))
    stdout:flush()
    local line = io.stdin:read("L")
    if not line then return nil end
    if line:sub(-1) == "\n" then line = line:sub(1, -2) end
    if not STDIN_TTY then stdout:write(line, "\n") end -- a readline-style front end echoes piped input
    if first and line:sub(1, 1) == "=" then line = "return " .. line:sub(2) end
    return line
  end
  local function loadline()
    local line = pushline(true)
    if not line then return nil end
    local f = load("return " .. line, "=stdin")
    if f then return f end
    while true do
      local f2, err = load(line, "=stdin")
      if f2 then return f2 end
      if err:sub(-5) ~= "<eof>" then return false, err end
      local more = pushline(false)
      if not more then return false, err end
      line = line .. "\n" .. more
    end
  end
  while true do
    local f, err = loadline()
    if f == nil then break end
    if f == false then
      message(err)
    else
      local r = table.pack(docall(f))
      if r[1] then
        if r.n > 1 then
          local ok, e = pcall(print, table.unpack(r, 2, r.n))
          if not ok then message(string.format("error calling 'print' (%s)", tostring(e))) end
        end
      else
        message(tostring(r[2]))
      end
    end
  end
  stdout:write("\n")
  stdout:flush()
end

local function main()
  if HAS_V then stdout:write(VERSION, "\n"); stdout:flush() end
  if not NOENV then
    setpath("path", "LUA_PATH")
    setpath("cpath", "LUA_CPATH")
    if not handle_init() then return false end
  end
  for _, a in ipairs(ACTIONS) do
    local kind, value = a[1], a[2]
    if kind == "W" then
      warn("@on")
    elseif kind == "e" then
      if not dostring(value, "=(command line)") then return false end
    elseif kind == "l" then
      if not dolibrary(value) then return false end
    end
  end
  if SCRIPT ~= nil then
    local name = SCRIPT
    if name == false then name = nil end
    if not dofile_(name, pushargs()) then return false end
  end
  if HAS_I then
    repl()
  elseif SCRIPT == nil and not HAS_E and not HAS_V then
    if STDIN_TTY then
      stdout:write(VERSION, "\n")
      repl()
    elseif not dofile_(nil) then
      return false
    end
  end
  return true
end

local ok, res = pcall(main)
if not ok then
  message(tostring(res))
  os.exit(1, true)
end
os.exit(res and 0 or 1, true)
""";
}
