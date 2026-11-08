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

`NetworkManager` is a Flax `GamePlugin`, so it initializes at the plugin level, not per-scene, and survives scene changes. Its config lives in a `ReflectSettings` asset so every scene uses the same values. In the Content window, create a Json asset of type `Reflect.ReflectSettings`, then add it in Game Settings under Custom Settings with the key `Reflect`. Fields:

- `Address` and `Port` for the connection endpoint (defaults `127.0.0.1:7777`).
- `MaxConnections` for the server cap (default 16).
- `SyncInterval` controls how often the server pushes SyncVar deltas, in seconds (default 0.1).
- `SpawnablePrefabs` is an array of prefabs that can be spawned over the network. Each prefab must have a `NetworkIdentity` component on its root.

Settings are re-read each time a server or client starts, so edits apply on the next start. Without the asset Reflect logs a warning and uses the defaults.

Add a `NetworkManagerUI` script to an actor in the scene each process starts in. It waits a short delay after the scene loads (0.25s), then calls `OnStart()` which checks the command line for `server` or `client`. The inspector's `Start Server` and `Start Client` buttons start those roles in the current process. The editor toolbar's `Start Server` button launches a separate server process, as described below.

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

### In the editor

1. Create the `ReflectSettings` asset (see above) and add `NetworkManagerUI` to the scene. Use `127.0.0.1` for the local server.
2. Save the scene and enter Play mode.
3. Click `Start Server` in the editor's top toolbar. The button is enabled only during Play mode.

Reflect launches another Flax Editor process with the same project and the first loaded scene. That process runs headless, with rendering and audio disabled, skips script compilation, and passes `server` to `NetworkManager.OnStart()`. It loads the saved scene asset rather than copying the editor's live scene, so save your changes before starting.

Once the server signals that startup is complete, Reflect calls `NetworkManager.StartClient()` in the editor. An already-active client is left alone. Server logs appear in the editor Output window with a `[Server]` prefix.

The toolbar button changes to `Stop Server`. Clicking it, leaving Play mode, or unloading the editor plugin stops the child process and disconnects the client that Reflect auto-started. If the server exits on its own, Reflect also cleans up that client and resets the button.

### From the command line

You can launch each role in a packaged game. Keep `NetworkManagerUI` in the startup scene so it calls `NetworkManager.OnStart()`:

```bash
# Start a server
YourGame.exe server

# Start a client (connects to 127.0.0.1:7777 by default)
YourGame.exe client
```

## Next steps

From here, read [Architecture](/architecture) to understand the layers, then dig into [SyncVars](/syncvars) and the [RPC System](/rpc-system) for the details.
