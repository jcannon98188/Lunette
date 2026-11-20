using System.Buffers;

namespace Lunette;

/// <summary>
/// Pooled byte accumulator for building a Lua string with exactly one result allocation. Always
/// <see cref="Dispose"/> it (use <c>using var</c>); it is a struct, so do not copy it.
/// </summary>
[LuaStringPath]
internal struct ByteBuilder : IDisposable
{
    private byte[]? _buf;
    private int _len;

    public ByteBuilder(int capacity)
    {
        _buf = ArrayPool<byte>.Shared.Rent(Math.Max(capacity, 16));
        _len = 0;
    }

    public readonly int Length => _len;
    public readonly ReadOnlySpan<byte> Bytes => new(_buf, 0, _len);

    private void Grow(int needed)
    {
        int size = Math.Max(_len + needed, (_buf?.Length ?? 0) * 2);
        byte[] bigger = ArrayPool<byte>.Shared.Rent(Math.Max(size, 16));
        if (_buf is not null)
        {
            _buf.AsSpan(0, _len).CopyTo(bigger);
            ArrayPool<byte>.Shared.Return(_buf);
        }
        _buf = bigger;
    }

    public void Append(byte b)
    {
        if (_buf is null || _len == _buf.Length) Grow(1);
        _buf![_len++] = b;
    }

    public void Append(ReadOnlySpan<byte> data)
    {
        if (_buf is null || _len + data.Length > _buf.Length) Grow(data.Length);
        data.CopyTo(_buf.AsSpan(_len));
        _len += data.Length;
    }

    public void AppendRepeated(ReadOnlySpan<byte> data, long times)
    {
        for (long i = 0; i < times; i++) Append(data);
    }

    /// <summary>Reserves <paramref name="count"/> bytes at the end and returns them for the caller to fill.</summary>
    public Span<byte> Reserve(int count)
    {
        if (_buf is null || _len + count > _buf.Length) Grow(count);
        var s = _buf.AsSpan(_len, count);
        _len += count;
        return s;
    }

    public void Truncate(int length) => _len = length;

    /// <summary>Copies the content into a new exactly sized Lua string, interning short ones in <paramref name="cache"/>.</summary>
    public readonly LuaString ToLuaString(StringCache? cache) =>
        cache is null ? LuaString.Create(Bytes) : cache.Intern(Bytes);

    public void Dispose()
    {
        if (_buf is not null) ArrayPool<byte>.Shared.Return(_buf);
        _buf = null;
        _len = 0;
    }
}
