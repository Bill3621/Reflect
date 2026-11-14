using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Reflect;

internal static class CollectionSerializers
{
    public static void TryRegister(Type type)
    {
        var method = type switch
        {
            { IsSZArray: true } => nameof(RegArray),
            { IsGenericType: true } when type.GetGenericTypeDefinition() == typeof(List<>) => nameof(RegList),
            _ => null,
        };
        if (method == null) return;
        var element = type.IsSZArray ? type.GetElementType() : type.GetGenericArguments()[0];
        typeof(CollectionSerializers).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(element!).Invoke(null, null);
    }

    private static void RegArray<T>() => Serializers.Reg<T[]>(WriteItems, ReadItems<T>);

    private static void RegList<T>() => Serializers.Reg<List<T>>(WriteItems, r => ReadItems<T>(r) is { } items ? [..items] : null);

    private static void WriteItems<T>(NetworkWriter w, IReadOnlyCollection<T> items)
    {
        w.WriteUIntVar(items == null ? 0 : (uint)items.Count + 1);
        if (items == null) return;
        var write = Serializer<T>.Write ?? throw new InvalidOperationException($"No serializer for {typeof(T).Name}");
        foreach (var item in items) write(w, item);
    }

    private static T[] ReadItems<T>(NetworkReader r)
    {
        var raw = r.ReadUIntVar();
        if (raw == 0) return null;
        if (raw - 1 > (uint)(r.EndPosition - r.Position))
            throw new InvalidDataException("Collection count exceeds the message size.");
        var read = Serializer<T>.Read ?? throw new InvalidOperationException($"No serializer for {typeof(T).Name}");
        var items = new T[raw - 1];
        for (var i = 0; i < items.Length; i++) items[i] = read(r);
        return items;
    }
}
