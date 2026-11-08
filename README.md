# Reflect

Client-server networking for Flax Engine. Inspired by Unity's [Mirror](https://github.com/MirrorNetworking/Mirror).

[Read the docs](https://bill3621.github.io/Reflect/)

Reflect wraps Flax's low-level `NetworkPeer` / ENet layer so you don't have to think about sockets, framing, or connection management. It started as the networking code for a multiplayer game, then got extracted into a standalone plugin.

## What's in here

- `ITransport` interface with two implementations: `FlaxTransport` (ENet/UDP) and `LoopbackTransport` (in-process, for testing)
- RPC attributes: `[Command]` (client to server), `[ClientRpc]` (server to all), `[TargetRpc]` (server to one). Each RPC picks its own channel type and authority requirements.
- `SyncVar<T>` with dirty tracking and delta serialization
- `SyncList<T>` and `SyncDictionary<TKey, TValue>` with operation-level delta tracking, plus read-only inspector editors
- `NetworkIdentity` and `NetworkScript` for spawn/despawn lifecycle on prefabs
- `NetworkTransform` with client-authoritative movement, snapshot interpolation, configurable send rate, and movement thresholds
- Interest management (`IInterestManagement`) with `GlobalInterest`, `DistanceInterest`, and `GridInterest` (spatial hash grid) implementations, so clients only receive objects they can actually see
- Server-driven scene changes (`Server.ChangeScene`), with late joiners loading the server's current scene
- `ReflectSettings` asset for project-wide config, with no scene setup needed to start a session
- Editor toolbar button that launches a separate headless server, auto-starts the local client, and forwards server logs
- Automatic enum serialization for RPC arguments, SyncVars, and synchronized collections
- Compact `NetworkWriter` / `NetworkReader` using varint encoding
- Per-packet reliability control, so state sync goes reliable and movement/voice goes unreliable

## Install

Drop the repo into your Flax project's `Plugins/` folder:

```bash
git submodule add https://github.com/Bill3621/Reflect.git Plugins/Reflect
```

## Usage

`NetworkManager` is a `GamePlugin` that initializes at the plugin level. Configure address, port, max connections, and spawnable prefabs in a `ReflectSettings` asset registered under Game Settings > Custom Settings as `Reflect`. Save the scene, enter Play mode, and click `Start Server` in the editor toolbar to launch a separate headless server. Reflect starts the editor's client after the server is ready.

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
        // Runs on all clients. Play animation, reset UI, etc.
    }
}
```

For packaged builds, start the server or client with command-line arguments (`server` / `client`). `NetworkManager.Instance.StartServer()` / `StartClient()` start a role from code. A process runs either the server or the client, never both. See [Getting started](https://bill3621.github.io/Reflect/getting-started) for the editor workflow.

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
│  │  ManagerUI   │  └────────────────────┘    │
│  │  Server      │                            │
│  │  Client      │  ┌────────────────────┐    │
│  │  Identity    │  │      Transport      │    │
│  │  Script      │  │  ITransport (iface) │    │
│  │  RpcRegistry │  │  FlaxTransport      │    │
│  │  NetworkRef  │  │  LoopbackTransport  │    │
│  │  InterestMgmt│  └────────────────────┘    │
│  └──────┬───────┘                            │
│  ┌──────┴───────┐                            │
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
