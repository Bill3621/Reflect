using System;
using System.Text;

namespace Reflect;

public sealed class NetworkWriter
{
    private byte[] _buf = new byte[1500];
    public int Position { get; private set; }

    public void Reset() => Position = 0;
    
    public ArraySegment<byte> ToSegment() => new(_buf, 0, Position);

    private void Ensure(int extra)
    {
        if (Position + extra <= _buf.Length) return;
        var n = _buf.Length * 2;
        while (n < Position + extra) n *= 2;
        Array.Resize(ref _buf, n);
    }

    public void WriteByte(byte v)
    {
        Ensure(1);
        _buf[Position++] = v;
    }

    public void WriteInt(int v) => WriteUIntVar(ZigZag(v));
    public void WriteLong(long v) => WriteULongVar(ZigZag(v));
    public void WriteFloat(float v)
    {
        Ensure(4);
        BitConverter.TryWriteBytes(new Span<byte>(_buf, Position, 4), v);
        Position += 4;
    }

    public void WriteUIntVar(uint v) => WriteULongVar(v);

    public void WriteULongVar(ulong v)
    {
        while (v >= 0x80)
        {
            WriteByte((byte)(v | 0x80));
            v >>= 7;
        }
        WriteByte((byte)v);
    }

    public void WriteString(string s)
    {
        if (s == null)
        {
            WriteUIntVar(0);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(s);
        WriteUIntVar((uint)(bytes.Length + 1));
        Ensure(bytes.Length);
        Array.Copy(bytes, 0, _buf, Position, bytes.Length);
        Position += bytes.Length;
    }

    public void WriteGuid(Guid g)
    {
        Ensure(16);
        g.TryWriteBytes(new Span<byte>(_buf, Position, 16));
        Position += 16;
    }

    public void WriteBytes(ArraySegment<byte> seg)
    {
        WriteUIntVar((uint)seg.Count);
        Ensure(seg.Count);
        Array.Copy(seg.Array!, seg.Offset, _buf, Position, seg.Count);
        Position += seg.Count;
    }

    public void Write<T>(T value) => Serializer<T>.Write(this, value);

    private int _msgLenPos;

    public void BeginMessage(MsgType type)
    {
        WriteByte((byte)type);
        _msgLenPos = Position;
        Ensure(2);
        _buf[Position++] = 0;
        _buf[Position++] = 0;
    }

    public void FinishMessage()
    {
        var bodyLen = Position - _msgLenPos - 2;
        var saved = Position;
        Position = _msgLenPos;
        _buf[Position++] = (byte)(bodyLen & 0xFF);
        _buf[Position++] = (byte)((bodyLen >> 8) & 0xFF);
        Position = saved;
    }

    private static uint ZigZag(int v) => (uint)((v << 1) ^ (v >> 31));
    private static ulong ZigZag(long v) => (ulong)((v << 1) ^ (v >> 63));
}