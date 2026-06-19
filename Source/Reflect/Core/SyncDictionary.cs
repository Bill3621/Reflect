using System;
using System.Collections;
using System.Collections.Generic;

namespace Reflect;

public sealed class SyncDictionary<TKey, TValue> : ISyncVar, IReadOnlyDictionary<TKey, TValue>
{
    private enum Op : byte
    {
        Set, Remove, Clear
    }

    private readonly Dictionary<TKey, TValue> _items = [];
    private readonly List<(Op, TKey key, TValue value)> _ops = [];

    public event Action<TKey, TValue> OnSet;
    public event Action<TKey, TValue> OnRemove;
    public event Action OnClear;
    
    public bool IsDirty => _ops.Count > 0;
    public void ClearDirty() => _ops.Clear();

    public TValue this[TKey key]
    {
        get => _items[key];
        set
        {
            _items[key] = value;
            _ops.Add((Op.Set, key, value));
        }
    }

    public void Add(TKey key, TValue value) => this[key] = value;

    public bool Remove(TKey key)
    {
        if (!_items.Remove(key)) return false;
        _ops.Add((Op.Remove, key, default));
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _ops.Add((Op.Clear, default, default));
    }

    public bool ContainsKey(TKey key) => _items.ContainsKey(key);
    public bool ContainsValue(TValue value) => _items.ContainsValue(value);
    public bool TryGetValue(TKey key, out TValue value) => _items.TryGetValue(key, out value);
    public int Count => _items.Count;
    public IEnumerable<TKey> Keys => _items.Keys;
    public IEnumerable<TValue> Values => _items.Values;
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    
    public void SerializeFull(NetworkWriter w)
    {
        w.WriteUIntVar((uint)_items.Count);
        foreach (var kv in _items)
        {
            w.Write(kv.Key);
            w.Write(kv.Value);
        }
    }
    public void DeserializeFull(NetworkReader r)
    {
        _items.Clear();
        var n = (int)r.ReadUIntVar();
        for (var i = 0; i < n; i++)
        {
            var k = r.Read<TKey>();
            var v = r.Read<TValue>();
            _items[k] = v;
        }
    }

    public void SerializeDelta(NetworkWriter w)
    {
        w.WriteUIntVar((uint)_ops.Count);
        foreach (var (op, key, value) in _ops)
        {
            w.WriteByte((byte)op);
            switch (op)
            {
                case Op.Set: w.Write(key); w.Write(value); break;
                case Op.Remove: w.Write(key); break;
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
                case Op.Set:
                {
                    var k = r.Read<TKey>();
                    var v = r.Read<TValue>();
                    _items[k] = v;
                    OnSet?.Invoke(k, v);
                    break;
                }
                case Op.Remove:
                {
                    var k = r.Read<TKey>();
                    if (_items.Remove(k, out var old))
                        OnRemove?.Invoke(k, old);
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
