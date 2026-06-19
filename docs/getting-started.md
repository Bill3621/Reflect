# Getting Started

Reflect is a Flax Engine plugin written in C#. It targets .NET 10 and Flax Engine 1.12. There is no native code to compile.

## Install the plugin

Add the repository as a submodule inside your project's `Plugins/` folder:

```bash
git submodule add https://github.com/Bill3621/Reflect.git Plugins/Reflect
```

A plain clone works too if you do not care about submodules. Just make sure the folder ends up at `Plugins/Reflect` so Flax picks it up.

## Wire it into your project

Flax needs to know about the plugin module in three places.

First, reference the plugin project from your game's `.flaxproj`. Open your `<Game>.flaxproj` and add the plugin to `References`:

```json
"References": [
  { "Name": "$(EnginePath)/Flax.flaxproj" },
  { "Name": "$(ProjectPath)/Plugins/Reflect/Reflect.flaxproj" }
]
```

Second, add the module dependency in your game module's `Build.cs` (for example `Source/Game/Game.Build.cs`):

```csharp
public override void Setup(BuildOptions options)
{
    base.Setup(options);

    options.PrivateDependencies.Add("Reflect");
}
```

Third, add the module to your game and editor targets so it gets linked into both builds:

```csharp
public class GameTarget : GameProjectTarget
{
    public override void Init()
    {
        base.Init();
        Modules.Add("Reflect");
    }
}
```

Do the same for your editor target (`GameProjectEditorTarget`). After that, regenerate the project scripts and rebuild. The `Reflect` namespace should now be available.

## Set up the NetworkManager

`NetworkManager` is a Flax `Script`. Add it to an actor in your main scene, or spawn it at runtime. It exposes a few fields in the inspector:

- `Address` and `Port` for the connection endpoint (defaults `127.0.0.1:7777`).
- `MaxConnections` for the server cap (default 16).
- `SyncInterval` controls how often the server pushes SyncVar deltas, in seconds (default 0.1).
- `SpawnablePrefabs` is an array of prefabs that can be spawned over the network. Each prefab must have a `NetworkIdentity` component on its root.

On awake, `NetworkManager` creates a `FlaxTransport` from those settings and holds a static `Instance` reference. On start it checks the command line for `server` or `client` and boots accordingly. You can also call `StartServer()` or `StartClient()` yourself (both have `[Button]` so they show up in the inspector).

## Write a NetworkScript

To put networking on an object, add a `NetworkIdentity` component to the prefab root and subclass `NetworkScript`. Here is a minimal health example with a server-authoritative respawn flow:

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

The client calls `RequestRespawn`, which sends a `[Command]` to the server. The server validates it (authority is checked automatically), resets health, and broadcasts a `[ClientRpc]` to every client so they all see the respawn at the same time. See the [RPC System](/rpc-system) page for the full picture.

## Run the server and client

You can launch each role from the command line. Reflect reads `server` and `client` as arguments in `NetworkManager.OnStart`:

```bash
# Start a server
YourGame.exe server

# Start a client (connects to 127.0.0.1:7777 by default)
YourGame.exe client
```

For testing in the editor, select the `NetworkManager` actor and use the `Start Server` / `Start Client` buttons. There is a `StartHost` method that runs both at once, but it is marked obsolete because the server and client share the same process and you will see spawned objects twice.

## Next steps

From here, read [Architecture](/architecture) to understand the layers, then dig into [SyncVars](/syncvars) and the [RPC System](/rpc-system) for the details.
