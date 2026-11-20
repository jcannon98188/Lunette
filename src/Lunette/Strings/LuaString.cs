using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text.Unicode;

namespace Lunette;

/// <summary>
/// A Lua string: an immutable byte sequence (never a .NET <c>string</c>). The representation is a single
/// <c>byte[]</c> with an 8-byte header (a lazily computed hash, then reserved flags) in front of the bytes,
/// held in the object slot of <see cref="LuaValue"/>. This struct is a thin accessor over it; the backing
/// array is only ever indexed here (analyzer LD003).
/// </summary>
[LuaStringPath]
internal readonly struct LuaString : IEquatable<LuaString>
{
    /// <summary>PUC's short-string limit: strings up to this length may be interned.</summary>
    public const int MaxShortLength = 40;

    private const int HashOffset = 0;
    private const int HeaderSize = 8;

    /// <summary>CI variant (LUNETTE_CONSTANT_HASH=1): every hash is the same, proving equality never relies on hashes alone. Fixed for the process so cached hashes stay consistent.</summary>
    internal static readonly bool ConstantHash = StringConfig.ConstantHash;

    private static readonly uint Seed = (uint)Random.Shared.Next();

    private readonly byte[] _a;

    internal LuaString(byte[] backing) => _a = backing;

    public static LuaString Empty { get; } = Allocate(0);

    /// <summary>The backing array, for storing in a <see cref="LuaValue"/>.</summary>
    internal byte[] Backing => _a;

    public bool IsDefault => _a is null;
    public int Length => _a.Length - HeaderSize;
    public ReadOnlySpan<byte> Bytes => new(_a, HeaderSize, _a.Length - HeaderSize);

    /// <summary>Writable view for builders; only valid until the string is first hashed or published.</summary>
    internal Span<byte> WritableBytes => new(_a, HeaderSize, _a.Length - HeaderSize);

    public byte this[int index] => _a[HeaderSize + index];

    /// <summary>Allocates a zero-filled string of <paramref name="length"/> bytes to be filled through <see cref="WritableBytes"/>.</summary>
    public static LuaString Allocate(int length) => new(new byte[HeaderSize + length]);

    public static LuaString Create(ReadOnlySpan<byte> data)
    {
        var s = Allocate(data.Length);
        data.CopyTo(s.WritableBytes);
        return s;
    }

    /// <summary>Encodes a .NET string as UTF-8 (the host boundary default).</summary>
    public static LuaString FromUtf16(ReadOnlySpan<char> text)
    {
        int n = 0;
        for (int i = 0; i < text.Length; i++) n += text[i] < 0x80 ? 1 : 3;
        // Upper bound is 3 bytes per char; encode into a scratch span then copy exactly.
        Span<byte> tmp = n <= 256 ? stackalloc byte[n] : new byte[n];
        Utf8.FromUtf16(text, tmp, out _, out int written, replaceInvalidSequences: true, isFinalBlock: true);
        return Create(tmp[..written]);
    }

    /// <summary>Raw-byte conversion: each char (0-255) becomes one byte.</summary>
    public static LuaString FromLatin1(ReadOnlySpan<char> text)
    {
        var s = Allocate(text.Length);
        var d = s.WritableBytes;
        for (int i = 0; i < text.Length; i++) d[i] = (byte)text[i];
        return s;
    }

    public int Hash
    {
        get
        {
            int h = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_a, HashOffset, 4));
            if (h != 0) return h;
            h = ComputeHash(Bytes);
            BinaryPrimitives.WriteInt32LittleEndian(new Span<byte>(_a, HashOffset, 4), h);
            return h;
        }
    }

    public static int ComputeHash(ReadOnlySpan<byte> data)
    {
        if (ConstantHash) return 1;
        uint h = Seed ^ (uint)data.Length;
        for (int i = 0; i < data.Length; i++) h ^= (h << 5) + (h >> 2) + data[i];
        int r = (int)h;
        return r == 0 ? 1 : r;
    }

    public bool Equals(LuaString other)
    {
        if (ReferenceEquals(_a, other._a)) return true;
        if (_a is null || other._a is null || _a.Length != other._a.Length) return false;
        // Hashes are only compared when both are already known; they are a cheap early-out, never the decision.
        int h1 = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(_a, HashOffset, 4));
        int h2 = BinaryPrimitives.ReadInt32LittleEndian(new ReadOnlySpan<byte>(other._a, HashOffset, 4));
        if (h1 != 0 && h2 != 0 && h1 != h2) return false;
        return Bytes.SequenceEqual(other.Bytes);
    }

    public override bool Equals(object? obj) => obj is LuaString s && Equals(s);
    public override int GetHashCode() => Hash;

    public static bool operator ==(LuaString a, LuaString b) => a.Equals(b);
    public static bool operator !=(LuaString a, LuaString b) => !a.Equals(b);

    /// <summary>Ordinal byte comparison (the "C" locale collation).</summary>
    public int CompareTo(LuaString other) => Bytes.SequenceCompareTo(other.Bytes);

    /// <summary>Decodes as UTF-8 for display; invalid bytes become U+FFFD.</summary>
    public override string ToString()
    {
        if (_a is null) return "";
        var b = Bytes;
        var chars = b.Length <= 256 ? stackalloc char[b.Length] : new char[b.Length];
        Utf8.ToUtf16(b, chars, out _, out int written, replaceInvalidSequences: true, isFinalBlock: true);
        return new string(chars[..written]);
    }

    /// <summary>One char per byte (Latin-1 view), used for numeric parsing and diagnostics.</summary>
    public string ToLatin1String()
    {
        var b = Bytes;
        var chars = b.Length <= 256 ? stackalloc char[b.Length] : new char[b.Length];
        for (int i = 0; i < b.Length; i++) chars[i] = (char)b[i];
        return new string(chars);
    }
}
