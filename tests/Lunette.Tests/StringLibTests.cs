using Xunit;

namespace Lunette.Tests;

/// <summary>The string and utf8 libraries, with expected values from the Lua 5.4 manual and reference behaviour.</summary>
public class StringLibTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    private static string RunWith(LuaState state, string source)
    {
        var output = new StringWriter { NewLine = "\n" };
        state.Output = output;
        state.DoString(source);
        return output.ToString();
    }

    [Theory]
    [InlineData("""print(("hello"):sub(2, 4), ("hello"):sub(-3), ("hello"):sub(0), ("hello"):sub(4, 2) == "", ("hello"):sub(-100, 100))""", "ell\tllo\thello\ttrue\thello")]
    [InlineData("""print(("MiXed"):upper(), ("MiXed"):lower(), ("abc"):reverse(), #("x"):rep(0))""", "MIXED\tmixed\tcba\t0")]
    [InlineData("""print(("ab"):rep(3, ","), ("ab"):rep(1, ","), ("ab"):rep(-1) == "")""", "ab,ab,ab\tab\ttrue")]
    [InlineData("""print(("ABC"):byte(), ("ABC"):byte(2, 3), ("ABC"):byte(-1), ("ABC"):byte(10))""", "65\t66\t67")]
    [InlineData("""print(string.char(72, 105), string.char() == "", #string.char(0, 255))""", "Hi\ttrue\t2")]
    [InlineData("""print(string.len("a\0b"), ("x"):len(), string.len(123))""", "3\t1\t3")]
    public void Basic_functions(string src, string expected) => Assert.Equal(expected + "\n", Run(src));

    [Theory]
    [InlineData("""print(("aXbXXc"):gsub("X", "-"))""", "a-b--c\t3")]
    [InlineData("""print(("aXbXXc"):gsub("XX", "[%0]"))""", "aXb[XX]c\t1")]
    [InlineData("""print(("abab"):gsub("ab", "%1%1", 1))""", "ababab\t1")]
    [InlineData("""print(("abc"):gsub("z", "-"))""", "abc\t0")]
    [InlineData("""print(("aXbX"):gsub("X", {X = "yy"}))""", "ayybyy\t2")]
    [InlineData("""print(("aXbX"):gsub("X", function(c) return c:lower() end))""", "axbx\t2")]
    [InlineData("""print(("aXb"):gsub("X", function() return false end))""", "aXb\t1")]
    [InlineData("""print(("abc"):gsub("", "-"))""", "-a-b-c-\t4")]
    public void Gsub_plain_patterns(string src, string expected) => Assert.Equal(expected + "\n", Run(src));

    [Fact]
    public void Single_byte_strings_are_equal_whatever_their_origin() =>
        Assert.Equal("true\ttrue\ttrue\n", Run("""
            local t = {}
            t[("abc"):sub(2, 2)] = 1
            print(t.b == 1, string.char(98) == ("a" .. "b"):sub(2), ("xbx"):match("b") == "b")
            """));

    [Fact]
    public void Format_integer_without_modifiers_covers_extremes() =>
        Assert.Equal("-9223372036854775808 9223372036854775807 0 -1\n",
            Run("""print(string.format("%d %i %d %d", math.mininteger, math.maxinteger, 0, -1))"""));

    [Fact]
    public void Case_conversion_is_ascii_only() =>
        Assert.Equal("éA\n", Run("""print(("\xC3\xA9a"):upper())"""));

    [Fact]
    public void Char_out_of_range_is_an_error() =>
        Assert.Contains("value out of range", Assert.Throws<LuaException>(() => Run("string.char(256)")).Message);

    [Theory]
    [InlineData("""print(string.format("%d %5d|%-5d|%05d %+d", 42, 42, 42, 42, 42))""", "42    42|42   |00042 +42")]
    [InlineData("""print(string.format("%x %X %#x %o %c", 255, 255, 255, 8, 65))""", "ff FF 0xff 10 A")]
    [InlineData("""print(string.format("%5.2f|%.0f|%10.3f|%-8.1f|", 3.14159, 2.5, -1.5, 2.25))""", " 3.14|2|    -1.500|2.2     |")]
    [InlineData("""print(string.format("%e %E %.2e", 12345.678, 0.00012, 5))""", "1.234568e+04 1.200000E-04 5.00e+00")]
    [InlineData("""print(string.format("%g %g %g %g %.3g", 100000, 1e20, 0.0001, 0.5, 1234.5678))""", "100000 1e+20 0.0001 0.5 1.23e+03")]
    [InlineData("""print(string.format("%a %a %.2a", 1, 0.5, 1.999))""", "0x1p+0 0x1p-1 0x2.00p+0")]
    [InlineData("""print(string.format("%s %s %s %10s|%-6s|%.2s", "a", nil, true, "right", "left", "truncate"))""", "a nil true      right|left  |tr")]
    [InlineData("""print(string.format("%q", 'a "quoted"\n\0z\r1'))""", "\"a \\\"quoted\\\"\\\n\\0z\\0131\"")]
    [InlineData("""print(string.format("%q %q %q", 1, 0.5, true))""", "1 0x1p-1 true")]
    [InlineData("""print(string.format("100%%"), string.format("%i", 7), string.format("%u", -1))""", "100%\t7\t18446744073709551615")]
    [InlineData("""print(string.format("%d", "10"), string.format("%s", 1.5), string.format("%d", 3.0))""", "10\t1.5\t3")]
    [InlineData("""print(string.format("%5s|%-5s|", 1, 2))""", "    1|2    |")]
    [InlineData("""print(string.format("%.14g", 0.1), string.format("%f", 1/0), string.format("%5.1f", -1/0), string.format("%.3f", 0/0 ~= 0/0 and 1 or 0))""", "0.1\tinf\t -inf\t1.000")]
    public void Format(string src, string expected) => Assert.Equal(expected + "\n", Run(src));

    [Theory]
    [InlineData("""string.format("%d", 1.5)""", "number has no integer representation")]
    [InlineData("""string.format("%d", "x")""", "number expected, got string")]
    [InlineData("""string.format("%d")""", "bad argument #2 to 'format' (no value)")]
    [InlineData("""string.format("%y", 1)""", "invalid conversion '%y' to 'format'")]
    [InlineData("""string.format("%q", {})""", "value has no literal form")]
    public void Format_errors(string src, string fragment) =>
        Assert.Contains(fragment, Assert.Throws<LuaException>(() => Run(src)).Message);

    [Theory]
    [InlineData("""print(("hello world"):find("wor"), ("hello"):find("l"), ("hello"):find("xyz"))""", "7\t3\tnil")]
    [InlineData("""print(("a.b"):find(".", 1, true), ("a.b"):find("%."), ("abc"):find("b", -1), ("abc"):find("", 10))""", "2\t2\tnil\tnil")]
    [InlineData("""print(("hello"):find("(l)(l)"), ("hello"):find("()ll()"))""", "3\t3\t4\t3\t5")]
    [InlineData("""print(("key=val"):match("(%w+)=(%w+)"), ("  trim  "):match("^%s*(.-)%s*$"), ("abc"):match("^b"))""", "key\ttrim\tnil")]
    [InlineData("""print(("2024-01-15"):match("(%d+)-(%d+)-(%d+)"))""", "2024\t01\t15")]
    [InlineData("""print(("THE (quick) fox"):find("%((%a+)%)"), ("x"):match(".-"), ("abc"):match("a(.*)c"))""", "5\t\tb")]
    [InlineData("""print(("f(a(b)c)d"):match("%b()"), ("THE END"):match("%f[%a]%a+", 4), ("a,b"):match("[^,]+"))""", "(a(b)c)\tEND\ta")]
    [InlineData("""print(("abcabc"):match("(abc)%1"), ("hello"):match(".-(l+)"), ("0x1F"):match("^0[xX](%x+)$"))""", "abc\tll\t1F")]
    public void Find_and_match(string src, string expected) => Assert.Equal(expected + "\n", Run(src));

    [Fact]
    public void Gmatch_iterates_over_matches() =>
        Assert.Equal("one,two,three,\nk1=v1;k2=v2;\n", Run("""
            local out = ""
            for w in ("one two three"):gmatch("%a+") do out = out .. w .. "," end
            print(out)
            out = ""
            for k, v in ("k1=v1, k2=v2"):gmatch("(%w+)=(%w+)") do out = out .. k .. "=" .. v .. ";" end
            print(out)
            """));

    [Fact]
    public void Gmatch_with_empty_matches_terminates() =>
        Assert.Equal("4\n", Run("""
            local n = 0
            for _ in ("abc"):gmatch("") do n = n + 1 end
            print(n)
            """));

    [Theory]
    [InlineData("""print(("hello world"):gsub("o", "0"))""", "hell0 w0rld\t2")]
    [InlineData("""print(("hello"):gsub("l", "L", 1))""", "heLlo\t1")]
    [InlineData("""print(("abc"):gsub("%w", "%0%0"), ("abc"):gsub("(a)(b)", "%2%1"), ("a b"):gsub("%s", "%%"))""", "aabbcc\tbac\ta%b\t1")]
    [InlineData("""print(("hello world"):gsub("%w+", string.upper))""", "HELLO WORLD\t2")]
    [InlineData("""print(("$name is $age"):gsub("%$(%w+)", {name = "Bob", age = 42}))""", "Bob is 42\t2")]
    [InlineData("""print(("abc"):gsub("", "-"), ("abc"):gsub("^a", "X"), ("abc"):gsub("b*", "-"))""", "-a-b-c-\tXbc\t-a-c-\t3")]
    [InlineData("""print(("x y"):gsub("%w", function(c) if c == "x" then return nil end return c:upper() end))""", "x Y\t2")]
    public void Gsub(string src, string expected) => Assert.Equal(expected + "\n", Run(src));

    [Theory]
    [InlineData("""("x"):find("[a")""", "malformed pattern (missing ']')")]
    [InlineData("""("x"):find("%")""", "malformed pattern (ends with '%')")]
    [InlineData("""("x"):find("(")""", "unfinished capture")]
    [InlineData("""("x"):find("%1")""", "invalid capture index %1")]
    [InlineData("""("x"):gsub("x", "%2")""", "invalid capture index %2")]
    [InlineData("""("x"):gsub("x", "%")""", "invalid use of '%' in replacement string")]
    public void Pattern_errors(string src, string fragment) =>
        Assert.Contains(fragment, Assert.Throws<LuaException>(() => Run(src)).Message);

    [Theory]
    [InlineData("""print(#string.pack("i4", 100), string.unpack("i4", string.pack("i4", -2)))""", "4\t-2\t5")]
    [InlineData("""print(string.unpack("<I2", "\1\2"), string.unpack(">I2", "\1\2"))""", "513\t258\t3")]
    [InlineData("""print(string.pack("z", "hi") == "hi\0", string.unpack("z", "hi\0rest"))""", "true\thi\t4")]
    [InlineData("""print(string.pack("s1", "abc") == "\3abc", string.unpack("s1", "\3abcd"))""", "true\tabc\t5")]
    [InlineData("""print(string.packsize("i4i8"), string.packsize("!8i4i8"), string.packsize("bhij"))""", "12\t16\t15")]
    [InlineData("""print(string.unpack("d", string.pack("d", 1.5)), string.unpack("f", string.pack("f", 0.25)))""", "1.5\t0.25\t5")]
    [InlineData("""print(string.pack(">i2", -2) == "\xFF\xFE", string.unpack("b", "\xFF"), string.unpack("B", "\xFF"))""", "true\t-1\t255\t2")]
    [InlineData("""print(string.unpack("c3", "abcdef"), string.unpack("<j", string.pack("<j", -9223372036854775807 - 1)) == -9223372036854775807 - 1)""", "abc\ttrue")]
    [InlineData("""print(string.pack("!4 i1 !4 i4", 1, 2) == "\1\0\0\0\2\0\0\0")""", "true")]
    public void Pack_and_unpack(string src, string expected) => Assert.Equal(expected + "\n", Run(src));

    [Theory]
    [InlineData("""string.pack("i1", 200)""", "integer overflow")]
    [InlineData("""string.pack("I1", -1)""", "unsigned overflow")]
    [InlineData("""string.unpack("i4", "ab")""", "data string too short")]
    [InlineData("""string.pack("i17", 1)""", "integral size (17) out of limits [1,16]")]
    [InlineData("""string.pack("q", 1)""", "invalid format option 'q'")]
    [InlineData("""string.packsize("s")""", "variable-length format")]
    public void Pack_errors(string src, string fragment) =>
        Assert.Contains(fragment, Assert.Throws<LuaException>(() => Run(src)).Message);

    [Theory]
    [InlineData("""print(utf8.char(72, 228, 8364, 128512) == "H\u{E4}\u{20AC}\u{1F600}", #utf8.char(0x10FFFF))""", "true\t4")]
    [InlineData("""print(utf8.len("h\u{E4}llo"), utf8.len("\xFF"), utf8.len("abc", 2), utf8.len("", 1))""", "5\tnil\t2\t0")]
    [InlineData("""print(utf8.codepoint("h\u{E4}\u{20AC}", 1, -1))""", "104\t228\t8364")]
    [InlineData("""print(utf8.offset("a\u{E4}b", 3), utf8.offset("a\u{E4}b", -1), utf8.offset("a\u{E4}b", 0, 3))""", "4\t4\t2")]
    [InlineData("""local t = {} for p, c in utf8.codes("a\u{E4}\u{20AC}") do t[#t + 1] = p .. ":" .. c end print(t[1], t[2], t[3])""", "1:97\t2:228\t4:8364")]
    [InlineData("""print(#utf8.charpattern, utf8.len("\u{D7FF}"), utf8.len("\xED\xA0\x80"), utf8.len("\xED\xA0\x80", 1, -1, true))""", "14\t1\tnil\t1")]
    public void Utf8(string src, string expected) => Assert.Equal(expected + "\n", Run(src));

    [Fact]
    public void Utf8_codepoint_of_invalid_data_is_an_error() =>
        Assert.Contains("invalid UTF-8 code", Assert.Throws<LuaException>(() => Run("""utf8.codepoint("\xFF")""")).Message);

    [Fact]
    public void Everything_works_with_a_capacity_one_intern_cache()
    {
        var state = new LuaState(1);
        Assert.Equal("true\tfalse\tabcdef\t3\n", RunWith(state, """
            local a, b = ("abc"):rep(1), "ab" .. "c"
            local t = {}
            t[a] = 1; t["x"] = 2; t["y"] = 3
            print(a == b and t[b] == 1, ("xy"):sub(1, 1) == "y", ("abc"):upper():lower() .. "def", (("a,b,c"):gsub(",", ";")) == "a;b;c" and 3)
            """));
    }

    [Fact]
    public void Intern_cache_returns_equal_content_but_equality_never_depends_on_it()
    {
        var cache = new StringCache(1);
        LuaString a = cache.Intern("hello"u8), b = cache.Intern("hello"u8);
        cache.Intern("other"u8);
        LuaString c = cache.Intern("hello"u8);
        Assert.True(a == b);
        Assert.True(a == c);
        Assert.True(a != cache.Intern("hellp"u8));
        Assert.Equal(a.Hash, c.Hash);
    }

    [Fact]
    public void Long_strings_are_not_interned_but_compare_by_content()
    {
        var cache = new StringCache();
        byte[] data = new byte[100];
        LuaString a = cache.Intern(data), b = cache.Intern(data);
        Assert.False(ReferenceEquals(a.Backing, b.Backing));
        Assert.True(a == b);
    }

    [Fact]
    public void Invalid_utf8_escape_bytes_survive_length_and_byte() =>
        Assert.Equal("2\t255\t254\n", Run("local s = '\\xFF\\xFE' print(#s, s:byte(1, -1))"));
}

