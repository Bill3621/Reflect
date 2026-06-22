using System;
using System.Collections;
using System.Collections.Generic;
using FlaxEngine;

namespace Reflect;

public sealed class SyncList<T> : ISyncVar, IReadOnlyList<T>
{
    private enum Op : byte
    {
        Add, Set, Insert, RemoveAt, Clear
    }

    private readonly List<T> _items = [];
    private readonly List<(Op, int index, T value)> _ops = [];

    public event Action<int, T> OnAdd;
    public event Action<int, T, T> OnSet;
    public event Action<int, T> OnRemove;
    public event Action OnClear;
    
    public bool IsDirty => _ops.Count > 0;
    public void ClearDirty() => _ops.Clear();

    public void Add(T item)
    {
        _items.Add(item);
        _ops.Add((Op.Add, _items.Count - 1, item));
    }

    public void Insert(int index, T item)
    {
        _items.Insert(index, item);
        _ops.Add((Op.Insert, index, item));
    }
    
    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        _ops.Add((Op.RemoveAt, index, default));
    }
    
    public bool Remove(T item)
    {
        var i = _items.IndexOf(item);
        if (i < 0) return false;
        RemoveAt(i);
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _ops.Add((Op.Clear, 0, default));
    }

    [NoSerialize]
    public T this[int index]
    {
        get => _items[index];
        set
        {
            _items[index] = value;
            _ops.Add((Op.Set, index, value));
        }
    }

    public int Count => _items.Count;
    public int IndexOf(T item) => _items.IndexOf(item);
    public bool Contains(T item) => _items.Contains(item);
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    
    public void SerializeFull(NetworkWriter w)
    {
        w.WriteUIntVar((uint)_items.Count);
        foreach (var item in _items)
            w.Write(item);
    }
    public void DeserializeFull(NetworkReader r)
    {
        _items.Clear();
        var n = (int)r.ReadUIntVar();
        for (var i = 0; i < n; i++)
            _items.Add(r.Read<T>());
    }

    public void SerializeDelta(NetworkWriter w)
    {
        w.WriteUIntVar((uint)_ops.Count);
        foreach (var (op, index, value) in _ops)
        {
            w.WriteByte((byte)op);
            switch (op)
            {
                case Op.Add: w.Write(value); break;
                case Op.Insert:
                case Op.Set: w.WriteUIntVar((uint)index); w.Write(value); break;
                case Op.RemoveAt: w.WriteUIntVar((uint)index); break;
                case Op.Clear: break;
             }
        }
    }

    public void DeserializeDelta(NetworkReader r)
    {
        var count = (int)r.ReadUIntVar();
        for (var n = 0; n < count; n++)
        {
            var op = (Op)r.ReadByte();
            switch (op)
            {
                case Op.Add:
                {
                    var v = r.Read<T>();
                    _items.Add(v);
                    OnAdd?.Invoke(_items.Count - 1, v);
                    break;
                }
                case Op.Insert:
                {
                    var i = (int)r.ReadUIntVar();
                    var v = r.Read<T>();
                    _items.Insert(i, v);
                    OnAdd?.Invoke(i, v);
                    break;
                }
                case Op.Set:
                {
                    var i = (int)r.ReadUIntVar();
                    var v = r.Read<T>();
                    var old = _items[i];
                    _items[i] = v;
                    OnSet?.Invoke(i, old, v);
                    break;
                }
                case Op.RemoveAt:
                {
                    var i = (int)r.ReadUIntVar();
                    var removed = _items[i];
                    _items.RemoveAt(i);
                    OnRemove?.Invoke(i, removed);
                    break;
                }
                case Op.Clear:
                    _items.Clear();
                    OnClear?.Invoke();
                    break;
            }
        }
    }
}
