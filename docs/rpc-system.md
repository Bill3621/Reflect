# RPC System

RPCs (remote procedure calls) are how gameplay code talks across the network. Reflect gives you three attributes, one per direction. You mark a method, and Reflect handles serialization, routing, and dispatch.

All three attributes live in the `Reflect` namespace and target methods only.

## The three attributes

### [Command]

`[Command]` marks a method that runs on the server. The owning client invokes it by calling `SendCommand(nameof(Method), args)`.

```csharp
[Command]
private void CmdRespawnRequest()
{
    Health.Value = 100;
}
```

`CommandAttribute` exposes two fields. `RequiresAuthority` defaults to `true`, which means the server checks that the sender actually owns this object before running the method. If a non-owner tries to fire it, the server logs a warning and drops the call. Set `RequiresAuthority = false` for things like chat or global requests. `ChannelType` defaults to `ReliableOrdered`.

### [ClientRpc]

`[ClientRpc]` marks a method that runs on every client that is observing the object. Only the server sends these. The body never runs on the server itself.

```csharp
[ClientRpc]
private void RpcOnRespawn()
{
    // Runs on all clients.
}
```

`ClientRpcAttribute` has a single `ChannelType` field, defaulting to `ReliableOrdered`.

### [TargetRpc]

`[TargetRpc]` marks a method that runs on exactly one client. The server picks the connection and passes it as the first argument to `SendTargetRpc`.

```csharp
[TargetRpc]
private void TargetScoreUpdate(int score)
{
    // Runs on the one client the server targeted.
}
```

`TargetRpcAttribute` has the same `ChannelType` field, defaulting to `ReliableOrdered`.

## Sending an RPC

Three protected methods on `NetworkScript` do the sending. Each takes the method name (use `nameof` to stay refactor-safe) and any arguments:

- `SendCommand(string methodName, params object[] args)` sends a `[Command]` from the client to the server.
- `SendClientRpc(string methodName, params object[] args)` sends a `[ClientRpc]` from the server to all observing clients.
- `SendTargetRpc(NetworkConnection target, string methodName, params object[] args)` sends a `[TargetRpc]` from the server to one connection.

Arguments are serialized with the registered `Serializer<T>` for each parameter type. The built-in types cover `bool`, `byte`, `int`, `uint`, `long`, `ulong`, `float`, `string`, `Guid`, `Float3`, and `Quaternion`. Passing a type with no registered serializer throws at send time.

`SendCommand` requires an active client. The other two require an active server. If the precondition is not met, Reflect logs an error and returns.

## A complete flow

Here is a respawn request from end to end. The owning client asks, the server resets state and replies, and every client reacts.

```csharp
using Reflect;

public class PlayerHealth : NetworkScript
{
    public readonly SyncVar<int> Health = new(100);

    // The owning client calls this directly (not an RPC).
    public void RequestRespawn()
    {
        SendCommand(nameof(CmdRespawnRequest));
    }

    // Runs on the server. Authority is checked automatically.
    [Command]
    private void CmdRespawnRequest()
    {
        Health.Value = 100;
        SendClientRpc(nameof(RpcOnRespawn));
    }

    // Runs on all clients.
    [ClientRpc]
    private void RpcOnRespawn()
    {
        // Play the respawn animation, reset the health bar, etc.
    }
}
```

The client never touches `Health.Value` directly here. It goes through the command, so the server stays authoritative over the value. Because `Health` is a SyncVar, the new value also propagates as a delta on the next sync tick to any client that missed the RPC.

## Picking a channel

Override the channel per RPC by setting `ChannelType` on the attribute. This is how `NetworkTransform` keeps movement cheap:

```csharp
[Command(ChannelType = NetworkChannelType.Unreliable)]
private void CmdMove(Float3 pos, Quaternion rot)
{
    Actor.Position = pos;
    Actor.Orientation = rot;
    SendClientRpc(nameof(RpcMove), pos, rot, false);
}
```

Use unreliable channels for high-frequency data where only the latest value matters and a dropped packet is harmless. Use reliable ordered (the default) for anything that must arrive and must be processed in sequence.

## How IDs are assigned

`RpcRegistry` assigns each RPC a `ushort` ID at runtime. The process is deterministic so that sender and receiver agree without exchanging a table.

When a type is first queried, `RpcRegistry.Build` reflects over the type and its base types (up to `NetworkScript`). It collects every method marked with an RPC attribute, sorts them by name using `StringComparer.Ordinal`, and assigns IDs zero, one, two, and so on in that sorted order.

Two consequences fall out of this. First, renaming a method shifts the sort order and changes IDs, which breaks compatibility with older clients or servers. Second, two methods with the same name in the same inheritance chain would collide. Keep your RPC method names unique and stable across versions.

The registry caches both a by-ID map (used on receive) and a by-name map (used on send). Reflection happens once per type, then never again.
