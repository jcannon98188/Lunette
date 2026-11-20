using System.Buffers;
using System.Text.Unicode;

namespace Lunette;

/// <summary>The filename boundary: Lua byte strings become .NET paths by strict UTF-8 decoding, never lossy replacement.</summary>
internal static class PathNames
{
    /// <summary>Decodes <paramref name="name"/> as strict UTF-8; false for invalid UTF-8 or an embedded NUL.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> name, out string path)
    {
        path = "";
        if (name.Contains((byte)0)) return false;
        char[] chars = ArrayPool<char>.Shared.Rent(name.Length);
        try
        {
            OperationStatus status = Utf8.ToUtf16(name, chars, out _, out int written, replaceInvalidSequences: false, isFinalBlock: true);
            if (status != OperationStatus.Done) return false;
            path = new string(chars, 0, written);
            return true;
        }
        finally { ArrayPool<char>.Shared.Return(chars); }
    }
}
