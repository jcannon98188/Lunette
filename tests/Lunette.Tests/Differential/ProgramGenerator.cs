using System.Text;

namespace Lunette.Tests.Differential;

/// <summary>
/// Seeded generator of deterministic Lua programs that probe areas where reimplementations drift: the number
/// tower, string and format calls, table and <c>#t</c> behaviour on sequences, metamethods, integer for loops
/// and goto. Every statement is wrapped in <c>pcall</c> and printed, so error messages are compared too.
/// Nothing generated depends on pairs order, addresses, time or the GC.
/// </summary>
public static class ProgramGenerator
{
    private static readonly string[] Numbers =
    [
        "0", "1", "-1", "2", "3", "7", "-7", "10", "255", "1000000", "math.maxinteger", "math.mininteger",
        "0.0", "-0.0", "0.5", "1.5", "-2.5", "1e15", "2^53", "1e100", "(1/0)", "(-1/0)", "3.0", "-3.0",
        "nil", "true", "{}",
    ];

    private static readonly string[] Numerics = [.. Numbers.Take(Numbers.Length - 3)];

    /// <summary>Integers and integral floats, for conversions that need an integer representation.</summary>
    private static readonly string[] IntArgs =
        ["0", "1", "-1", "2", "7", "255", "1000000", "math.maxinteger", "math.mininteger", "3.0", "-3.0", "65"];

    /// <summary>
    /// Strings are kept out of arithmetic operands: the engine words string-arithmetic errors differently
    /// (see corpus/known-string-arith-message.lua), so they only reach the tostring/tonumber/format probes.
    /// </summary>
    private static readonly string[] Texts = ["'10'", "'0x10'", "' 5 '", "'1e2'", "'abc'", "''"];

    private static readonly string[] BinaryOps =
        ["+", "-", "*", "/", "//", "%", "^", "&", "|", "~", "<<", ">>", "..", "==", "~=", "<", "<=", ">", ">=", "and", "or"];

    private static readonly string[] UnaryOps = ["-", "~", "not ", "#"];

    private static readonly string[] Formats =
        ["%d", "%5d", "%-5d|", "%05d", "%x", "%X", "%o", "%c", "%s", "%10s", "%-10s|", "%.3s", "%q", "%g", "%.3g", "%e", "%.2f", "%10.4f", "%i", "%%", "%5.1f", "%.0f"];

    private static readonly string[] Words = ["hello", "world", "lua", "", "a,b,c", "  pad  ", "x1y22z333", "CamelCase"];

    private static readonly string[] Patterns =
        ["%a+", "%d+", "(%w+)", "^h", "l+", "[abc]", "[^,]+", "%s*", "(.)(.)", "()", "%f[%w]%w+", "%bxy", ".-", "a?b*"];

    /// <summary>A non-tail call, so builtin errors are named and positioned by the call site as in the reference.</summary>
    private static string Call(string fn, string args) =>
        $"try(function() local a, b, c, d = {fn}({args}) return a, b, c, d end)\n";

    public static string Generate(int seed, int statements = 60)
    {
        var rng = new Random(seed);
        string Pick(string[] a) => a[rng.Next(a.Length)];
        string Any() => rng.Next(3) == 0 ? Pick(Texts) : Pick(Numbers);
        var sb = new StringBuilder();
        // NaN prints as -nan or nan depending on how it was produced (corpus/known-nan-sign.lua), so it is normalised here.
        sb.Append("local function show(...) local t = table.pack(...) for i = 1, t.n do if t[i] ~= t[i] then t[i] = 'NaN' end end print(table.unpack(t, 1, t.n)) end\n");
        sb.Append("local function try(f, ...) local a, n = {...}, select('#', ...) show(pcall(function() local r = table.pack(f(table.unpack(a, 1, n))) return table.unpack(r, 1, r.n) end)) end\n");
        for (int i = 0; i < statements; i++)
        {
            switch (rng.Next(10))
            {
                case 0 or 1 or 2:
                    sb.Append($"try(function() return {Pick(Numbers)} {Pick(BinaryOps)} {Pick(Numbers)} end)\n");
                    break;
                case 3:
                    sb.Append($"try(function() return {Pick(UnaryOps)}({Pick(Numbers)}) end)\n");
                    break;
                case 4:
                    {
                        // Argument errors from string builtins lack the caller position in the engine
                        // (corpus/known-builtin-argument-error-position.lua), so generated calls use valid arguments.
                        string format = Pick(Formats);
                        string arg = format == "%c" ? "65" // bytes above 127 would hit the non-ASCII output divergence
                            : format.Contains('s') || format.Contains('q') ? Pick(Texts)
                            : format is "%%" ? "nil"
                            : format.Contains('d') || format.Contains('x') || format.Contains('X') || format.Contains('o')
                              || format.Contains('c') || format.Contains('i') ? Pick(IntArgs)
                            : Pick(Numerics);
                        sb.Append(Call("string.format", $"'{format}', {arg}"));
                    }
                    break;
                case 5:
                    sb.Append(Call("string.find", $"'{Pick(Words)}', '{Pick(Patterns)}'"));
                    sb.Append(Call("string.gsub", $"'{Pick(Words)}', '{Pick(Patterns)}', '<%0>'"));
                    break;
                case 6:
                    sb.Append($"try(function() local t = {{}} for i = 1, {rng.Next(0, 9)} do t[#t + 1] = i * {rng.Next(1, 5)} end " +
                              $"{(rng.Next(2) == 0 ? "table.remove(t)" : "table.insert(t, 1, 99)")} " +
                              "return #t, table.concat(t, ',') end)\n");
                    break;
                case 7:
                    sb.Append($"try(function() local mt = {{__add = function(a, b) return 'add' end, __concat = function(a, b) return 'cat' end, " +
                              "__len = function() return 42 end, __eq = function() return true end, __lt = function() return true end, " +
                              "__le = function() return false end, __call = function(self, x) return 'called', x end, __index = function(t, k) return k end, " +
                              "__tostring = function() return 'OBJ' end} " +
                              $"local a, b = setmetatable({{}}, mt), setmetatable({{}}, mt) return a + {Pick(Numbers)}, a .. b, #a, a == b, a < b, a <= b, a(5), a.zzz, tostring(a) end)\n");
                    break;
                case 8:
                    sb.Append($"try(function() local n = 0 for i = {Pick(Numbers)}, {Pick(Numbers)}, {Pick(Numbers)} do n = n + 1 if n > 5 then break end end return n end)\n");
                    sb.Append($"try(function() local out = {{}} for i = {rng.Next(-3, 4)}, {rng.Next(-3, 6)} do if i % 2 == 0 then goto continue end out[#out + 1] = i ::continue:: end return table.concat(out, ' ') end)\n");
                    break;
                default:
                    sb.Append($"try(function() return tostring({Any()}), tonumber({Any()}), math.tointeger({Any()}), math.type({Any()}) end)\n");
                    sb.Append(Call("string.rep", $"'{Pick(Words)}', {rng.Next(-1, 4)}, '-'"));
                    sb.Append(Call("string.sub", $"'{Pick(Words)}', {rng.Next(-6, 7)}, {rng.Next(-6, 7)}"));
                    break;
            }
        }
        return sb.ToString();
    }
}
