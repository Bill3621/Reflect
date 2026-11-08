using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Reflect;

public class NetworkManager : GamePlugin
{
    public static NetworkManager Instance { get; private set; }

    /// <summary>Config read from the <see cref="ReflectSettings"/> asset when a session starts.</summary>
    public ReflectSettings Settings { get; private set; } = new();

    public event Action OnServerStarted;

    public static bool IsServer => Instance?.Server is { Active: true };
    public static bool IsClient => Instance?.Client is { Active: true };

    public NetworkServer Server { get; private set; }

    public NetworkClient Client { get; private set; }

    private ITransport _transport;
    private readonly Dictionary<Guid, Prefab> _registry = [];
    private float _accum;

    public override void Initialize()
    {
        Instance = this;
        Scripting.Update += OnUpdate;
        Level.SceneLoaded += OnSceneLoaded;
        Serializers.Init();
    }

    /// <summary>Starts the command-line role once the first scene loads in play mode. Runs once per process.</summary>
    private void OnSceneLoaded(Scene scene, Guid sceneId)
    {
        if (!Engine.IsPlayMode) return;
        Level.SceneLoaded -= OnSceneLoaded;
        StartFromCommandLine();
    }

    /// <summary>Starts the role given on the command line (<c>server</c> or <c>client</c>), if any.</summary>
    private void StartFromCommandLine()
    {
        var args = Engine.CommandLine;
        if (args.Contains("server"))
        {
            StartServer();
            Console.Error.WriteLine("REFLECT_SERVER_READY");
            Console.Error.Flush();
        } else if (args.Contains("client"))
        {
            StartClient();
        }
    }

    /// <summary>
    /// Re-reads <see cref="ReflectSettings"/> and rebuilds the prefab registry and transport.
    /// Does nothing while a session is active, since both are in use. Start calls this, so edits apply on the next start.
    /// </summary>
    [Button]
    public void ReloadSettings()
    {
        if (Server is { Active: true } || Client is { Active: true }) return;
        Settings = ReflectSettings.Load();

        _registry.Clear();
        foreach (var prefab in Settings.SpawnablePrefabs ?? [])
            if (prefab)
                _registry[prefab.ID] = prefab;

        _transport = new FlaxTransport
        {
            Address = Settings.Address,
            Port = Settings.Port,
            MaxConnections = Settings.MaxConnections,
        };
    }

    [Button]
    public void StartServer()
    {
        if (Server is { Active: true }) return;
        if (IsClient)
        {
            Debug.LogWarning("Cannot start a server while a client is running in this process.");
            return;
        }
        ReloadSettings();
        _accum = 0;
        Server = new NetworkServer(_transport, _registry)
        {
            Interest = new GridInterest()
        };
        Server.Start();
        OnServerStarted?.Invoke();
    }

    [Button]
    public void StartClient()
    {
        if (Client is { Active: true }) return;
        if (IsServer)
        {
            Debug.LogWarning("Cannot start a client while a server is running in this process.");
            return;
        }
        ReloadSettings();
        Client = new NetworkClient(_transport, _registry);
        Client.Connect();
    }

    private void OnUpdate()
    {
        _transport?.Poll();

        if (!IsServer) return;
        _accum += Time.DeltaTime;
        if (_accum < Settings.SyncInterval) return;
        _accum = 0;
        Server.Update();
        _transport?.Poll();
    }

    public override void Deinitialize()
    {
        Scripting.Update -= OnUpdate;
        Level.SceneLoaded -= OnSceneLoaded;
        Client?.Disconnect();
        Server?.Stop();
        Client = null;
        Server = null;
        _transport = null;
        _registry.Clear();
        _accum = 0;
        if (Instance == this) Instance = null;
    }
}
