using System;
using System.IO;
using System.Text;

namespace Reflect;

public sealed class NetworkReader
{
    private byte[] _buf;
    private int _pos;
    private int _len;

    public int Position => _pos;

    public void SetBuffer(ArraySegment<byte> seg)
    {
        _buf = seg.Array ?? Array.Empty<byte>();
        _pos = seg.Offset;
        _len = seg.Offset + seg.Count;
    }

    public bool HasMore => _pos < _len;

    private void Require(int length)
    {
        if (length < 0 || length > _len - _pos)
            throw new InvalidDataException("Read exceeds the network message boundary.");
    }

    public byte ReadByte()
    {
        Require(1);
        return _buf[_pos++];
    }

    public int ReadInt() => UnZigZag(ReadUIntVar());
    public long ReadLong() => UnZigZag(ReadULongVar());

    public float ReadFloat()
    {
        Require(4);
        var v = BitConverter.ToSingle(_buf, _pos);
        _pos += 4;
        return v;
    }

    public uint ReadUIntVar()
    {
        var value = ReadULongVar();
        if (value > uint.MaxValue)
            throw new InvalidDataException("Network varint exceeds 32 bits.");
        return (uint)value;
    }

    public ulong ReadULongVar()
    {
        ulong v = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var b = ReadByte();
            if (shift == 63 && b > 1)
                throw new InvalidDataException("Network varint exceeds 64 bits.");
            v |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return v;
        }
        throw new InvalidDataException("Unterminated network varint.");
    }

    public string ReadString()
    {
        var len = ReadUIntVar();
        if (len == 0) return null;
        var bytes = ReadSegment((int)(len - 1));
        return Encoding.UTF8.GetString(bytes.Array, bytes.Offset, bytes.Count);
    }

    public Guid ReadGuid()
    {
        var bytes = ReadSegment(16);
        return new Guid(new ReadOnlySpan<byte>(bytes.Array, bytes.Offset, bytes.Count));
    }

    public ArraySegment<byte> ReadBytes() => ReadSegment((int)ReadUIntVar());

    // Slice a bounded message body without exposing bytes from the next message.
    internal ArraySegment<byte> ReadSegment(int length)
    {
        Require(length);
        var segment = new ArraySegment<byte>(_buf, _pos, length);
        _pos += length;
        return segment;
    }

    public T Read<T>() => Serializer<T>.Read(this);

    public int ReadLength()
    {
        var lo = ReadByte();
        var hi = ReadByte();
        return lo | (hi << 8);
    }
    public int EndPosition => _len;
    public void SkipRemaining(int msgEnd) => ReadSegment(msgEnd - _pos);

    private static int UnZigZag(uint v) => (int)(v >> 1) ^ -(int)(v & 1);
    private static long UnZigZag(ulong v) => (long)(v >> 1) ^ -(long)(v & 1);
}
