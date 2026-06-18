using System;
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
        _buf = seg.Array;
        _pos = seg.Offset;
        _len = seg.Offset + seg.Count;
    }
    
    public bool HasMore => _pos < _len;

    public byte ReadByte() => _buf[_pos++];
    public int ReadInt() => UnZigZag((uint)ReadULongVar());
    public long ReadLong() => UnZigZag(ReadULongVar());

    public float ReadFloat()
    {
        var v = BitConverter.ToSingle(_buf, _pos);
        _pos += 4;
        return v;
    }

    public uint ReadUIntVar() => (uint)ReadULongVar();

    public ulong ReadULongVar()
    {
        ulong v = 0;
        var shift = 0;
        while (true)
        {
            var b = ReadByte();
            v |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return v;
    }

    public string ReadString()
    {
        var len = ReadUIntVar();
        if (len == 0) return null;
        len -= 1;
        var s = Encoding.UTF8.GetString(_buf, _pos, (int)len);
        _pos += (int)len;
        return s;
    }

    public Guid ReadGuid()
    {
        var g = new Guid(new ReadOnlySpan<byte>(_buf, _pos, 16));
        _pos += 16;
        return g;
    }

    public ArraySegment<byte> ReadBytes()
    {
        var len = (int)ReadUIntVar();
        var se = new ArraySegment<byte>(_buf, _pos, len);
        _pos += len;
        return se;
    }
    
    public T Read<T>() => Serializer<T>.Read(this);

    public int ReadLength()
    {
        var lo = ReadByte();
        var hi = ReadByte();
        return lo | (hi << 8);
    }
    public int EndPosition => _len;
    public void SkipRemaining(int msgEnd) => _pos = msgEnd;

    private static int UnZigZag(uint v) => (int)(v >> 1) ^ -(int)(v & 1);
    private static long UnZigZag(ulong v) => (long)(v >> 1) ^ -(long)(v & 1);
}