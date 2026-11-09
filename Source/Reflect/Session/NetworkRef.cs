using System;

namespace Reflect;

/// <summary>
/// A serializable reference to a networked object. Only the NetId goes on the wire.
/// <see cref="Resolve()"/> looks it up in the Spawned dictionary on each call.
/// </summary>
public readonly struct NetworkRef : IEquatable<NetworkRef>
{
    public readonly uint NetId;

    public NetworkRef(uint netId) => NetId = netId;

    public NetworkRef(NetworkIdentity identity) => NetId = identity != null ? identity.NetId : 0;

    public bool IsNull => NetId == 0;

    /// <summary>
    /// Looks up the live identity on this peer. Returns null for a null ref or a target that hasn't spawned here.
    /// Nothing is cached, so the result stays correct after a despawn.
    /// </summary>
    public NetworkIdentity Resolve()
    {
        if (NetId == 0) return null;
        var nm = NetworkManager.Instance;
        if (nm == null) return null;

        // Check whichever endpoint this process runs.
        if (NetworkManager.IsServer && nm.Server.Spawned.TryGetValue(NetId, out var s))
            return s;
        if (NetworkManager.IsClient && nm.Client.Spawned.TryGetValue(NetId, out var c))
            return c;
        return null;
    }

    /// <summary>
    /// Resolves the target and returns its script of type <typeparamref name="T"/>, or null.
    /// </summary>
    public T Resolve<T>() where T : NetworkScript
    {
        var ni = Resolve();
        return ni != null ? ni.Actor.GetScript<T>() : null;
    }

    public bool Equals(NetworkRef other) => NetId == other.NetId;
    public override bool Equals(object obj) => obj is NetworkRef r && Equals(r);
    public override int GetHashCode() => (int)NetId;
    public override string ToString() => IsNull ? "NetworkRef(null)" : $"NetworkRef({NetId})";

    // Convenience implicit conversions.
    public static implicit operator NetworkRef(NetworkIdentity ni) => new(ni);

    public static implicit operator NetworkRef(NetworkScript ns) => new(ns != null ? ns.Identity : null);
}