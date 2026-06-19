using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Reflect;

public class NetworkManager : Script
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

    public override void OnAwake()
    {
        Instance = this;
        Serializers.Init();

        _registry = new Dictionary<Guid, Prefab>();
        if(SpawnablePrefabs != null)
            foreach (var prefab in SpawnablePrefabs)
                if (prefab)
                    _registry[prefab.ID] = prefab;

        _transport = new FlaxTransport
        {
            Address = Address,
            Port = Port,
            MaxConnections = MaxConnections,
        };
    }

    public override void OnStart()
    {
        var args = Engine.CommandLine;
        if (args.Contains("server"))
        {
            StartServer();
        } else if (args.Contains("client"))
        {
            StartClient();
        }
    }

    [Button]
    //[Obsolete("Don't use this please, it causes you to see stuff double because you are the server AND the client so it spawns prefabs for example twice.")]
    public void StartHost()
    {
        StartServer();
        StartClient();
    }

    [Button]
    public void StartServer()
    {
        Server = new NetworkServer(_transport, _registry);
        Server.Start();
        OnServerStarted?.Invoke();
    }
    
    [Button]
    public void StartClient()
    {
        Client = new NetworkClient(_transport, _registry);
        Client.Connect();
    }

    public override void OnUpdate()
    {
        _transport?.Poll();

        if (!IsServer) return;
        _accum += Time.DeltaTime;
        if (_accum < SyncInterval) return;
        _accum = 0;
        Server.Update();
        _transport?.Poll();
    }

    public override void OnDestroy()
    {
        Client?.Disconnect();
        Server?.Stop();
        if (Instance == this) Instance = null;
    }
}
