# Reflect

Client-server networking for Flax Engine. Inspired by Unity's [Mirror](https://github.com/MirrorNetworking/Mirror).

Reflect wraps Flax's low-level `NetworkPeer` / ENet layer so you don't have to think about sockets, framing, or connection management. It started as the networking code for a multiplayer game, then got extracted into a standalone plugin.

## What's in here

- `ITransport` interface with two implementations: `FlaxTransport` (ENet/UDP) and `LoopbackTransport` (in-process, for testing)
- RPC attributes: `[Command]` (client to server), `[ClientRpc]` (server to all), `[TargetRpc]` (server to one). Each RPC picks its own channel type and authority requirements.
- `SyncVar<T>` with dirty tracking and delta serialization
- `NetworkIdentity` and `NetworkScript` for spawn/despawn lifecycle on prefabs
- `NetworkTransform` with client-authoritative movement, snapshot interpolation, configurable send rate, and movement thresholds
- Compact `NetworkWriter` / `NetworkReader` using varint encoding
- Per-packet reliability control, so state sync goes reliable and movement/voice goes unreliable

## Install

Drop the repo into your Flax project's `Plugins/` folder:

```bash
git submodule add https://github.com/Bill3621/Reflect.git Plugins/Reflect
```

## Usage

Add `NetworkManager` as a Script in your scene. Set the address, port, max connections, and assign network-spawned prefabs to `SpawnablePrefabs`.

To network an object, put `NetworkIdentity` on the prefab root and subclass `NetworkScript`:

```csharp
using Reflect;

public class PlayerHealth : NetworkScript
{
    public readonly SyncVar<int> Health = new(100);

    // Called by the owning client when they want to respawn
    public void RequestRespawn()
    {
        SendCommand(nameof(CmdRespawnRequest));
    }

    [Command]
    private void CmdRespawnRequest()
    {
        Health.Value = 100;
        SendClientRpc(nameof(RpcOnRespawn));
    }

    [ClientRpc]
    private void RpcOnRespawn()
    {
        // Runs on all clients — play animation, reset UI, etc.
    }
}
```

Start the server or client from command line args (`server` / `client`) or use the editor buttons on `NetworkManager`.

## Architecture

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
│  └──────┬───────┘  │  FlaxTransport      │    │
│         │          │  LoopbackTransport  │    │
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

## License

MIT
