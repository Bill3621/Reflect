using System;
using FlaxEngine;

namespace Reflect;

public class NetworkManagerUI : Script
{
    [Tooltip("Prefabs that can be spawned over the network.")]
    public Prefab[] SpawnablePrefabs;
    
    public float SyncInterval = 0.1f;

    public string Address = "127.0.0.1";
    public ushort Port = 7777;
    public ushort MaxConnections = 16;

    [Header("States")]
    [ShowInEditor] public bool Initialized => NetworkManager.Instance != null;
    [ShowInEditor] public bool IsHost => NetworkManager.IsHost;
    [ShowInEditor] public bool IsServer => NetworkManager.IsServer;
    [ShowInEditor] public bool IsClient => NetworkManager.IsClient;

    public override void OnAwake() => RebuildNetworkManager();
    

    private void RebuildNetworkManager()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.SpawnablePrefabs = SpawnablePrefabs;
        NetworkManager.Instance.SyncInterval = SyncInterval;
        NetworkManager.Instance.Address = Address;
        NetworkManager.Instance.Port = Port;
        NetworkManager.Instance.MaxConnections = MaxConnections;
        NetworkManager.Instance.RebuildPrefabRegistry();
        NetworkManager.Instance.RebuildTransport();
    }
    
    [Button]
    [Obsolete("Some scripts might need to be adapted to work properly with this mode")]
    public void StartHost()
    {
        RebuildNetworkManager();
        NetworkManager.Instance.StartHost();
    }

    [Button]
    public void StartServer()
    {
        RebuildNetworkManager();
        NetworkManager.Instance.StartServer();
    }
    
    [Button]
    public void StartClient()
    {
        RebuildNetworkManager();
        NetworkManager.Instance.StartClient();
    }
    
    [Button]
    public void StopServer() => NetworkManager.Instance.Server.Stop();
    [Button]
    public void StopClient() => NetworkManager.Instance.Client.Disconnect();
    
}
