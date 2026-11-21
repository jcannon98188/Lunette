using System.Text;
using Lunette.Compiler;
using Xunit;

namespace Lunette.Tests;

/// <summary>
/// Ticket 57: the lexer allocates nothing per token once a name has been seen (per-compilation name table, lazy token text),
/// and the "near" text of every error is unchanged.
/// </summary>
public class LexerAllocationTests
{
    private const string Line = "local alpha, beta = gamma.delta[12] + 3.5 * (alpha - 0x1F) .. \"str\" ; if beta ~= nil then return alpha == beta end\n";

    private static (int tokens, long bytes) Lex(string source)
    {
        byte[] src = Encoding.Latin1.GetBytes(source);
        var cache = new StringCache();
        var lex = new Lexer(src, "=t", cache);
        int n = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        while (lex.Next().Kind != Tok.Eof) n++;
        return (n, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void RepeatedTokensAllocateNothingPerToken()
    {
        string source = string.Concat(Enumerable.Repeat(Line, 2000));
        Lex(source); // warm the JIT
        var (tokens, bytes) = Lex(source);
        Assert.True(tokens > 40000);
        // Table growth and the first sighting of each distinct name are fixed costs; per token the budget is a few bytes at most.
        Assert.True(bytes < tokens * 4L, $"{bytes} bytes for {tokens} tokens");
    }

    [Theory]
    [InlineData("x = = 1", "[string \"x = = 1\"]:1: unexpected symbol near '='")]
    [InlineData("x = 1 + + \"abc\"", "[string \"x = 1 + + \"abc\"\"]:1: unexpected symbol near '+'")]
    [InlineData("return 0x1g", "[string \"return 0x1g\"]:1: malformed number near '0x1g'")]
    [InlineData("for = 1", "[string \"for = 1\"]:1: <name> expected near '='")]
    [InlineData("x = \"abc\" \"def\"", "[string \"x = \"abc\" \"def\"\"]:1: unexpected symbol near '\"def\"'")]
    [InlineData("x = 12 34", "[string \"x = 12 34\"]:1: unexpected symbol near '34'")]
    [InlineData("x = [[long]] [[more]]", "[string \"x = [[long]] [[more]]\"]:1: unexpected symbol near '[[more]]'")]
    public void ErrorTextsQuoteTheRawToken(string source, string expected)
    {
        var ex = Assert.Throws<LuaException>(() => LuaCompiler.Compile(source, source));
        Assert.Equal(expected, ex.Message);
    }
}
