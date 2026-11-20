using Xunit;

namespace Lunette.Tests;

/// <summary>Byte strings: literals, escapes, long strings, equality, ordering, keys, concatenation and metatable access.</summary>
public class StringTests
{
    private static string Run(string source) => ControlFlowTests.Run(source);

    [Fact]
    public void Length_counts_bytes_not_characters() =>
        Assert.Equal("5\t2\t3\n", Run("""print(#"hello", #"\xC3\xA9", #"\u{20AC}")"""));

    [Theory]
    [InlineData("""print(#"a\0b")""", "3")]
    [InlineData("""print(("\65\066\x43\z   D"):byte(1, -1))""", "65\t66\t67\t68")]
    [InlineData("""print(("\u{7FFFFFFF}"):byte(1, -1))""", "253\t191\t191\t191\t191\t191")]
    [InlineData("""print(("\a\b\f\n\r\t\v\\\""):byte(1, -1))""", "7\t8\t12\t10\t13\t9\t11\t92\t34")]
    public void Escapes(string src, string expected) => Assert.Equal(expected + "\n", Run(src));

    [Fact]
    public void Backslash_newline_in_a_literal_is_a_newline() =>
        Assert.Equal("97\t10\t98\n", Run("print((\"a\\\nb\"):byte(1, -1))"));

    [Fact]
    public void Long_strings_skip_the_first_newline_and_ignore_escapes() =>
        Assert.Equal("a\\nb\nc\t4\n", Run("local s = [[\na\\nb\nc]]\nlocal t = [==[ ]] ]==]\nprint(s, #t)"));

    [Fact]
    public void Equality_is_by_content()
    {
        Assert.Equal("true\tfalse\ttrue\n", Run("""
            local a = "abc" .. "def"
            print(a == "abcdef", a == "abcdeg", ("x"):rep(100) == ("x"):rep(100))
            """));
    }

    [Fact]
    public void Ordering_is_bytewise() =>
        Assert.Equal("true\ttrue\tfalse\ttrue\ttrue\n", Run("""print("a" < "b", "a" < "aa", "b" < "a", "a\0b" > "a\0a", "Z" <= "a")"""));

    [Fact]
    public void Strings_are_table_keys_by_content() =>
        Assert.Equal("1\t2\t3\n", Run("""
            local t = {}
            t["ab" .. "c"] = 1
            t[("x"):rep(50)] = 2
            t["\0z"] = 3
            print(t.abc, t[("x"):rep(50)], t["\0z"])
            """));

    [Fact]
    public void Concatenation_of_strings_and_numbers() =>
        Assert.Equal("a1\t1b\t1.5x\t12\t-0.0\n", Run("""print("a" .. 1, 1 .. "b", 1.5 .. "x", 1 .. 2, -0.0 .. "")"""));

    [Fact]
    public void Concatenation_is_right_associative() =>
        Assert.Equal("abcd\n", Run("""print("a" .. "b" .. "c" .. "d")"""));

    [Fact]
    public void Concat_uses_the_metamethod_for_other_types() =>
        Assert.Equal("cat\tcat\n", Run("""
            local mt = {__concat = function(a, b) return "cat" end}
            local t = setmetatable({}, mt)
            print(t .. "x", "x" .. t)
            """));

    [Fact]
    public void Concat_without_metamethod_is_an_error() =>
        Assert.Contains("attempt to concatenate a nil value",
            Assert.Throws<LuaException>(() => Run("""local x = "a" .. nil""")).Message);

    [Fact]
    public void Strings_have_a_shared_metatable_with_index_to_the_string_table() =>
        Assert.Equal("true\tHELLO\t5\n", Run("""
            print(getmetatable("").__index == string, ("hello"):upper(), ("hello"):len())
            """));

    [Fact]
    public void Tostring_and_tonumber_round_trip() =>
        Assert.Equal("10\t255\t0.5\tnil\t1e+100\tabc\n", Run("""
            print(tostring(10), tonumber("0xff"), tonumber("  0.5 "), tonumber("abc"), tostring(1e100), tostring("abc"))
            """));

    [Fact]
    public void Tonumber_with_a_base() =>
        Assert.Equal("255\t5\tnil\t-8\n", Run("""print(tonumber("ff", 16), tonumber("101", 2), tonumber("12", 2), tonumber("-10", 8))"""));

    [Fact]
    public void Non_ascii_source_bytes_survive_literals() =>
        Assert.Equal("2\t195\t169\n", Run("print(#\"é\", (\"é\"):byte(1, 2))"));

    [Fact]
    public void Printing_decodes_bytes_as_utf8() =>
        Assert.Equal("é€\n", Run("""print("\xC3\xA9\u{20AC}")"""));
}
