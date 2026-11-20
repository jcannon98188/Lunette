namespace Lunette.Compiler;

/// <summary>The printable form of a chunk name, after luaO_chunkid (LUA_IDSIZE is 60).</summary>
internal static class ChunkId
{
    private const int IdSize = 60;

    public static string Display(string source)
    {
        if (source.Length > 0 && source[0] == '=')
            return source.Length <= IdSize ? source[1..] : source.Substring(1, IdSize - 1);
        if (source.Length > 0 && source[0] == '@')
        {
            if (source.Length <= IdSize) return source[1..];
            return "..." + source[^(IdSize - 4)..];
        }
        const int room = IdSize - 15;
        int nl = source.IndexOf('\n');
        if (source.Length < room && nl < 0) return $"[string \"{source}\"]";
        int len = nl >= 0 ? nl : source.Length;
        if (len > room) len = room;
        return $"[string \"{source[..len]}...\"]";
    }
}
