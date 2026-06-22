using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Reflect;

public class NetworkManager : GamePlugin
{
    public static NetworkManager Instance { get; private set; }

    [Tooltip("Prefabs that can be spawned over the network.")]
    public Prefab[] SpawnablePrefabs;

    public float SyncInterval = 0.1f;

    public string Address = "127.0.0.1";
    public ushort Port = 7777;
    public ushort MaxConnections = 16;
    
    public event Action OnServerStarted;
    
    public static bool IsServer => Instance?.Server is { Active: true };
    public static bool IsClient => Instance?.Client is { Active: true };
    public static bool IsHost => IsServer && IsClient;

    public NetworkServer Server { get; private set; }

    public NetworkClient Client { get; private set; }

    private ITransport _transport;
    private Dictionary<Guid, Prefab> _registry;
    private float _accum;

    public override void Initialize()
    {
        Instance = this;
        Scripting.Update += OnUpdate;
        Serializers.Init();

        RebuildPrefabRegistry();
        RebuildTransport();
    }

    private bool _hasStarted;
    public void OnStart()
    {
        if (_hasStarted) return;
        _hasStarted = true;
        var args = Engine.CommandLine;
        if (args.Contains("server"))
        {
            StartServer();
        } else if (args.Contains("client"))
        {
            StartClient();
        }
    }

    public void RebuildPrefabRegistry()
    {
        _registry = new Dictionary<Guid, Prefab>();
        if (SpawnablePrefabs == null) return;
        foreach (var prefab in SpawnablePrefabs)
            if (prefab)
                _registry[prefab.ID] = prefab;
    }
    
    [Button]
    public void RebuildTransport()
    {
        _transport = new FlaxTransport
        {
            Address = Address,
            Port = Port,
            MaxConnections = MaxConnections,
        };
    }
    
    [Button]
    [Obsolete("Some scripts might need to be adapted to work properly with this mode")]
    public void StartHost()
    {
        StartServer();
        StartClient();
    }

    [Button]
    public void StartServer()
    {
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
        Client = new NetworkClient(_transport, _registry);
        Client.Connect();
    }

    private void OnUpdate()
    {
        _transport?.Poll();

        if (!IsServer) return;
        _accum += Time.DeltaTime;
        if (_accum < SyncInterval) return;
        _accum = 0;
        Server.Update();
        _transport?.Poll();
    }

    public override void Deinitialize()
    {
        Scripting.Update -= OnUpdate;
        Client?.Disconnect();
        Server?.Stop();
        if (Instance == this) Instance = null;
    }
}
