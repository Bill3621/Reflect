# Architecture

Reflect is organized in three layers. Your game code sits on top, the Reflect core sits in the middle, and Flax's ENet transport sits at the bottom. This page walks through what each layer does and how data moves between them.

## The layers

```
┌─────────────────────────────────────────────┐
│                Your Game                     │
│         (NetworkScript subclasses)           │
├─────────────────────────────────────────────┤
│                  Reflect                     │
│  ┌─────────────┐  ┌────────────────────┐    │
│  │  Session     │  │    Components      │    │
│  │  Manager     │  │  NetworkTransform  │    │
│  │  Server      │  └────────────────────┘    │
│  │  Client      │                            │
│  │  Identity    │  ┌────────────────────┐    │
│  │  Script      │  │      Transport      │    │
│  │  RpcRegistry │  │  ITransport (iface) │    │
│  │  NetworkRef  │  │  FlaxTransport      │    │
│  └──────┬───────┘  │  LoopbackTransport  │    │
│  ┌──────┴───────┐  └────────────────────┘    │
│  │    Core       │                           │
│  │  Writer/Reader│                           │
│  │  Serializer   │                           │
│  │  SyncVar      │                           │
│  └──────────────┘                            │
├─────────────────────────────────────────────┤
│          Flax Engine (ENet/UDP)              │
└─────────────────────────────────────────────┘
```

Your game scripts are plain `NetworkScript` subclasses. They declare `SyncVar<T>` fields and methods marked with RPC attributes. Reflect never asks you to touch sockets or frames. Everything below your scripts is the plugin.

The Session layer (`NetworkManager`, `NetworkServer`, `NetworkClient`, `NetworkIdentity`, `NetworkScript`, `RpcRegistry`, `NetworkRef`) owns the gameplay-facing API. The Core layer (`NetworkWriter`, `NetworkReader`, `Serializer`, `SyncVar`) handles encoding. The Transport layer is a thin abstraction over Flax's `NetworkPeer`.

## The transport abstraction

`ITransport` is the boundary between Reflect and the wire. It is a small interface with server and client halves. Server methods are `ServerStart`, `ServerSend(connectionId, data, channelType)`, `ServerStop`, plus events for connect, data, and disconnect. Client methods mirror that with `ClientConnect`, `ClientSend`, `ClientDisconnect`.

Reflect ships two implementations. `FlaxTransport` wraps Flax's `NetworkPeer` and `ENetDriver` for real network play. `LoopbackTransport` keeps everything in-process with a pair of queues, which is useful for unit tests and local prototyping. `NetworkManager` constructs a `FlaxTransport` for you, but you can hand either implementation to `NetworkServer` or `NetworkClient` directly.

See the [Transport Layer](/transport) page for the full interface and configuration details.

## The message pipeline

Every piece of data Reflect sends goes through the same pipeline. A sender builds a `NetworkWriter`, writes a header byte and a 2-byte body length, then writes the payload. The finished byte array becomes an `ArraySegment<byte>` passed to `ITransport`. On the receiving side, `Poll` drains the transport, and a `NetworkReader` walks the buffer one message at a time.

The on-wire format for a single message is a `MsgType` byte, a 16-bit little-endian body length, then the body. `NetworkWriter.BeginMessage` and `FinishMessage` handle the length back-patching for you. `NetworkReader.ReadLength` and `SkipRemaining` let the receive loop skip past a message it does not recognize without desyncing.

Transport implementations are free to frame the payload however they like. `FlaxTransport` writes a 4-byte length prefix on top (see [Transport Layer](/transport)) because that is what Flax's `NetworkMessage` expects. Reflect's own framing sits inside that payload.

## RPC dispatch

RPCs are discovered by reflection and dispatched by integer ID. When a type is first touched, `RpcRegistry.Build` scans its methods for `[Command]`, `[ClientRpc]`, and `[TargetRpc]` attributes, sorts them by name with an ordinal comparer, and assigns each one an incrementing `ushort` ID starting at zero.

At runtime, `SendCommand` (or `SendClientRpc` / `SendTargetRpc`) looks up the method by name, writes the object's `NetId`, the `ComponentIndex`, the RPC ID, and the serialized arguments, then hands the buffer to the transport. The receiver reads those same fields, finds the `NetworkIdentity` by `NetId`, indexes into its `Scripts` array with `ComponentIndex`, and calls `DispatchRpc` which invokes the method via reflection.

The ordinal sort is what keeps IDs stable. As long as method names do not collide across your inheritance chain, the ID for a given method name is the same on every machine. Renaming a method changes its sort position and therefore its ID, which breaks compatibility with older builds.

## Connection lifecycle

When a client connects, two things happen in sequence, and they fire different events.

`OnPlayerConnected` fires immediately. The connection is registered and its `NetworkConnection` object is in the `Connections` dictionary. But the client does not yet know about any spawned objects. The server queues the connection as pending.

On the next `Update` tick, the server drains the pending queue. For each pending connection, it sends a `Spawn` message for every existing object in the world, then sends `WelcomeDone`. After that, `OnPlayerLoaded` fires.

This split exists because spawning the entire world into a new client takes a full update tick. If `OnPlayerConnected` fired after the world was sent, gameplay code waiting for "player joined" would race against the spawn messages arriving on the client. The two-event model lets you react to the raw connection immediately (`OnPlayerConnected`) and to the ready-to-play state when the client has the full world (`OnPlayerLoaded`).

Most gameplay code wants `OnPlayerLoaded`. Use `OnPlayerConnected` only if you need to do something before the world spawn begins, like assigning metadata or logging.

## Host mode

`StartHost()` starts the server and client in the same process. This is useful for single-player, local co-op, or a listen-server setup where one player hosts and plays.

The naive approach (just running both) has a problem: the server creates objects in the scene, then sends spawn messages to the client, which creates the same objects again. Two copies of everything. Reflect avoids this by sharing state.

When `IsHost` is true, the client's `Spawned` dictionary delegates to the server's. They are the same dictionary. Spawn and despawn messages arriving from the transport are skipped by the client because the server already created (or removed) the object locally. State sync messages are skipped too, since the server's SyncVars are already at their current values in shared memory.

RPCs are the exception. They still go through the transport and arrive via the client's message handler, because RPCs are one-shot events rather than state. The host's commands travel to the server, and the server's ClientRpcs travel back to the host's client, same as any other client.

Ownership in host mode is set directly. When the server calls `SendSpawn` for the host's connection, it sets `IsOwnedLocally` on the `NetworkIdentity` right there instead of writing it to the wire for the client to read back. This is why `NetworkTransform` checks `IsLocalOwner` before applying position from a `CmdMove` on the host: the server already moved the object locally, so applying it again would double the movement.

## SyncVar dirty tracking

`SyncVar<T>` wraps a value and a dirty flag. Setting `Value` marks the flag only when the new value actually differs from the old one (using `EqualityComparer<T>.Default`). The server walks every spawned `NetworkIdentity` on each tick and calls `SerializeDelta` on scripts that report `AnyDirty`.

Delta serialization is a 64-bit mask. Reflect collects the dirty SyncVars on a script, sets the corresponding bits in a `ulong`, and writes the mask followed by only the dirty values. After writing, each dirty SyncVar clears its flag. Full serialization (used on spawn) writes the mask with all bits set for that script's SyncVar count, then every value. Because the mask is 64 bits wide, a single `NetworkScript` supports at most 64 SyncVars.

`SyncList<T>` and `SyncDictionary<TKey, TValue>` use the same dirty-mask slot as a regular `SyncVar<T>`, but their delta format is an operation log rather than a single value. The list tracks add, insert, set, removeAt, and clear. The dictionary tracks set, remove, and clear. Both clear their operation log after each delta serialization.

See [SyncVars](/syncvars) for the API, change hooks, and collection types.

## NetworkTransform snapshots

`NetworkTransform` is a `NetworkScript` subclass that synchronizes position and rotation. With `ClientAuthority` on (the default), the owning client sends its transform to the server through a `[Command]`. The server then broadcasts a `[ClientRpc]` to the other clients. When authority is off, the server drives movement and clients just receive.

Movement updates use the unreliable channel. Each client that is not the owner keeps a small ring buffer of snapshots and interpolates between them. The `InterpolationDelay` field (default 0.1 seconds) renders the object slightly in the past, which smooths out jitter and packet loss. Two thresholds (`PositionThreshold`, `RotationThreshold`) suppress updates when the object has barely moved, so a stationary object does not spam the network.

A teleport path clears the snapshot buffer and snaps the object directly, which avoids the interpolation system dragging the object across the map after a jump.

## Referencing objects across the network

Sometimes a script needs to point at another networked object. Maybe a projectile tracks its target, or a player holds a reference to the flag they are carrying. You cannot send a `NetworkIdentity` reference over the wire because the object on the other side lives at a different memory address (or may not exist yet).

`NetworkRef` solves this. It is a small readonly struct that stores a `NetId` and resolves to the live `NetworkIdentity` on demand. You create one from any `NetworkIdentity` or `NetworkScript` via the implicit conversion operators, pass it as an RPC argument or store it in a SyncVar, and call `Resolve()` on the receiving side to get the real object back.

```csharp
[Command]
private void CmdAssignTarget(NetworkRef target)
{
    var enemy = target.Resolve<EnemyHealth>();
    if (enemy != null)
        enemy.TakeDamage(10);
}
```

`Resolve()` checks the spawned dictionary every time you call it. It does not cache, so it stays correct if the target despawns and something else takes its `NetId`. If the object has not spawned on this peer yet, `Resolve()` returns `null`, so null-check the result.

## Channel types

Each message picks a `NetworkChannelType`. This is a Flax enum with four values:

- `ReliableOrdered` is the default for almost everything. State sync, RPCs, spawn, and despawn all use it. Messages arrive, in order, and are retried until acknowledged.
- `ReliableUnordered` gives reliability without ordering guarantees. Useful when order does not matter but loss is unacceptable.
- `Unreliable` drops messages that do not arrive. No ordering or retransmission.
- `UnreliableOrdered` drops lost messages but orders the ones that do arrive by sequence number.

The rule of thumb is simple. Game state that must arrive goes reliable ordered. High-frequency data where the latest value is all that matters (position, rotation, voice) goes unreliable.
