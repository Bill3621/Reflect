using System;

namespace Reflect;

/// <summary>
/// A serializable reference to a networked object. Stores netId on the wire,
/// resolves to the live NetworkIdentity lazily via the spawned dictionary.
/// </summary>
public readonly struct NetworkRef : IEquatable<NetworkRef>
{
    public readonly uint NetId;

    public NetworkRef(uint netId) => NetId = netId;

    public NetworkRef(NetworkIdentity identity) => NetId = identity != null ? identity.NetId : 0;

    public bool IsNull => NetId == 0;

    /// <summary>
    /// Resolve to the live identity on this peer. Returns null if the target
    /// hasn't spawned here yet (or is null). Always re-resolves — never caches,
    /// so it stays correct across despawn/respawn.
    /// </summary>
    public NetworkIdentity Resolve()
    {
        if (NetId == 0) return null;
        var nm = NetworkManager.Instance;
        if (nm == null) return null;

        // Look in whichever side(s) we're running.
        if (NetworkManager.IsServer && nm.Server.Spawned.TryGetValue(NetId, out var s))
            return s;
        if (NetworkManager.IsClient && nm.Client.Spawned.TryGetValue(NetId, out var c))
            return c;
        return null;
    }

    /// <summary>
    /// Resolve and fetch a specific NetworkScript off the target.
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