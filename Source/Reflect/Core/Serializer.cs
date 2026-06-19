using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Reflect;

public delegate void WriteFunc<in T>(NetworkWriter w, T value);
public delegate T ReadFunc<out T>(NetworkReader r);

public static class Serializer<T>
{
    public static WriteFunc<T> Write;
    public static ReadFunc<T> Read;
}

public static class Serializers
{
    private static bool _init;
    private static readonly Dictionary<Type, Action<NetworkWriter, object>> Writers = [];
    private static readonly Dictionary<Type, Func<NetworkReader, object>> Readers = [];

    public static void Init()
    {
        if (_init) return;
        _init = true;
        
        Reg<bool>((w, v) => w.WriteByte((byte)(v ? 1 : 0)), r => r.ReadByte() != 0);
        Reg<byte>((w, v) => w.WriteByte(v), r => r.ReadByte());
        Reg<int>((w, v) => w.WriteInt(v), r => r.ReadInt());
        Reg<uint>((w, v) => w.WriteUIntVar(v), r => r.ReadUIntVar());
        Reg<long>((w, v) => w.WriteLong(v), r => r.ReadLong());
        Reg<ulong>((w, v) => w.WriteULongVar(v), r => r.ReadULongVar());
        Reg<float>((w, v) => w.WriteFloat(v), r => r.ReadFloat());
        Reg<string>((w, v) => w.WriteString(v), r => r.ReadString());
        Reg<Guid>((w, v) => w.WriteGuid(v), r => r.ReadGuid());

        Reg<Float3>((w, v) => { w.WriteFloat(v.X); w.WriteFloat(v.Y); w.WriteFloat(v.Z); }, r => new Float3(r.ReadFloat(), r.ReadFloat(), r.ReadFloat()));
        Reg<Quaternion>((w, v) => { w.WriteFloat(v.X); w.WriteFloat(v.Y); w.WriteFloat(v.Z); w.WriteFloat(v.W); }, r => new Quaternion(r.ReadFloat(), r.ReadFloat(), r.ReadFloat(), r.ReadFloat()));

        Reg<NetworkRef>((w, v) => w.WriteUIntVar(v.NetId), r => new NetworkRef(r.ReadUIntVar()));
    }

    private static void Reg<T>(WriteFunc<T> w, ReadFunc<T> r)
    {
        Serializer<T>.Write = w;
        Serializer<T>.Read = r;
        Writers[typeof(T)] = (writer, o) => w(writer, (T)o);
        Readers[typeof(T)] = reader => r(reader);
    }

    public static void WriteBoxed(NetworkWriter w, Type t, object v)
    {
        if (!Writers.TryGetValue(t, out var fn))
            throw new InvalidOperationException($"No serializer for {t.Name}");
        fn(w, v);
    }
    
    public static object ReadBoxed(NetworkReader r, Type t)
    {
        return !Readers.TryGetValue(t, out var fn) ? throw new InvalidOperationException($"No serializer for {t.Name}") : fn(r);
    }
}