using System;
using System.Collections.Generic;

namespace Reflect;

public interface ISyncVar
{
    bool IsDirty { get; }
    void ClearDirty();
    
    void SerializeFull(NetworkWriter w);
    void SerializeDelta(NetworkWriter w);
    
    void DeserializeFull(NetworkReader r);
    void DeserializeDelta(NetworkReader r);
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

    public void SerializeFull(NetworkWriter w) => w.Write(_value);
    public void SerializeDelta(NetworkWriter w) => w.Write(_value);

    public void DeserializeFull(NetworkReader r) => Apply(r);
    public void DeserializeDelta(NetworkReader r) => Apply(r);

    private void Apply(NetworkReader r)
    {
        var old = _value;
        _value = r.Read<T>();
        if(!Cmp.Equals(old, _value))
            hook?.Invoke(old, _value);   
    }
    
    public static implicit operator T(SyncVar<T> v) => v._value;
    public override string ToString() => _value?.ToString() ?? "null";
}
