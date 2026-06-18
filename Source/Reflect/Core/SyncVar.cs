using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Reflect;

public interface ISyncVar
{
    bool IsDirty { get; }
    void ClearDirty();
    void Serialize(NetworkWriter w);
    void Deserialize(NetworkReader r);
}

public sealed class SyncVar<T>(T initial = default, Action<T, T> hook = null) : ISyncVar
{
    private T _value = initial;
    private static readonly EqualityComparer<T> Cmp = EqualityComparer<T>.Default;

    public T Value
    {
        get => _value;
        set
        {
            if(Cmp.Equals(_value, value)) return;
            _value = value;
            IsDirty = true;
        }
    }

    public bool IsDirty { get; private set; }

    public void ClearDirty() => IsDirty = false;

    public void Serialize(NetworkWriter w) => w.Write(_value);

    public void Deserialize(NetworkReader r)
    {
        var old = _value;
        _value = r.Read<T>();
        if(!Cmp.Equals(old, _value))
            hook?.Invoke(old, _value);   
    }
    
    public static implicit operator T(SyncVar<T> v) => v._value;
    public override string ToString() => _value?.ToString() ?? "null";
}
