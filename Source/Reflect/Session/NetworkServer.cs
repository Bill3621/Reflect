using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FlaxEngine;
using FlaxEngine.Networking;
using Debug = FlaxEngine.Debug;
using Object = FlaxEngine.Object;

namespace Reflect;

public sealed class NetworkServer(ITransport transport, Dictionary<Guid, Prefab> prefabs)
{
    private readonly NetworkWriter _w = new();
    private readonly NetworkReader _packetReader = new();
    private readonly NetworkReader _r = new();
    
    private readonly Queue<NetworkConnection> _pendingConns = [];

    public readonly Dictionary<uint, NetworkIdentity> Spawned = [];
    public readonly Dictionary<int, NetworkConnection> Connections = [];

    public IInterestManagement Interest = new GlobalInterest();
    private readonly Stopwatch _rebuildTimer = Stopwatch.StartNew();
    public float RebuildInterval = 1.0f;
    
    private uint _nextNetId = 1;
    
    public bool Active { get; private set; }
    public event Action<NetworkConnection> OnPlayerConnected;
    public event Action<NetworkConnection> OnPlayerLoaded;
    #nullable enable
    public event Action<NetworkConnection?>? OnPlayerDisconnected;
    #nullable disable

    public void Start()
    {
        transport.OnServerConnected += OnConnected;
        transport.OnServerData += OnData;
        transport.OnServerDisconnected += OnDisconnected;
        transport.ServerStart();
        Active = true;
        RegisterSceneObjects();
    }

    private void RegisterSceneObjects()
    {
        foreach (var ni in Level.GetScripts<NetworkIdentity>().Where(x => x.SceneId != 0))
        {
            ni.NetId = _nextNetId++;
            Spawned[ni.NetId] = ni;
            foreach (var s in ni.Scripts) s.OnNetworkSpawn();
        }
    }

    public void Stop()
    {
        transport.ServerStop();
        transport.OnServerConnected -= OnConnected;
        transport.OnServerData -= OnData;
        transport.OnServerDisconnected -= OnDisconnected;
        Active = false;
    }

    private void OnConnected(int connId)
    {
        Debug.Log($"Client connected with connId={connId}");
        var conn = new NetworkConnection { Id = connId };
        Connections[connId] = conn;
        _pendingConns.Enqueue(conn);
        
        OnPlayerConnected?.Invoke(conn);
    }

    private void OnDisconnected(int connId)
    {
        Debug.Log($"Client disconnected with connId={connId}");
        if (Connections.TryGetValue(connId, out var conn) && conn.OwnedPlayer != null)
            Despawn(conn.OwnedPlayer);
        Connections.Remove(connId);
        
        OnPlayerDisconnected?.Invoke(conn);
    }

    public void SendToObservers(NetworkIdentity ni, ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered)
    {
        foreach (var conn in Connections.Values.Where(conn => conn.Observing.Contains(ni.NetId)))
            transport.ServerSend(conn.Id, data, channelType);
    }

    public void SendToConnection(NetworkConnection conn, ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered)
    {
        if (conn != null)
            transport.ServerSend(conn.Id, data, channelType);
    }
    
    private void OnData(int connId, ArraySegment<byte> data)
    {
        if (!Connections.ContainsKey(connId)) return;
        _packetReader.SetBuffer(data);
        try
        {
            while (_packetReader.HasMore)
            {
                var type = Msg.ReadHeader(_packetReader);
                _r.SetBuffer(_packetReader.ReadSegment(_packetReader.ReadLength()));
                switch (type)
                {
                    case MsgType.Command: HandleCommand(connId); break;
                    default: Debug.LogWarning($"Server got unexpected msg {type}"); break;
                }
            }
        }
        catch (InvalidDataException ex)
        {
            Debug.LogWarning($"Dropped invalid packet from conn {connId}: {ex.Message}");
        }
    }
    
    private void HandleCommand(int connId)
    {
        var netId = _r.ReadUIntVar();
        var comp = _r.ReadByte();
        var rpcId = _r.ReadUIntVar();
        var argSeg = _r.ReadBytes();
        if (_r.HasMore)
            throw new InvalidDataException("Command has trailing data.");

        if (!Spawned.TryGetValue(netId, out var ni))
        {
            Debug.LogWarning($"Command for unknown netId {netId} (skipped)");
            return;
        }

        var scripts = ni.Scripts;
        if (comp >= scripts.Length)
            throw new InvalidDataException($"Command has invalid component index {comp}.");
        var script = scripts[comp];
        if (rpcId > ushort.MaxValue || !RpcRegistry.Get(script.GetType()).TryGetValue((ushort)rpcId, out var info))
            throw new InvalidDataException($"Command has unknown RPC ID {rpcId}.");
        if (info.Kind != RpcKind.Command)
        {
            Debug.LogWarning($"Conn {connId} tried invoking non-Command RPC '{info.Method.Name}'.");
            return;
        }

        if (info.RequiresAuthority)
        {
            Connections.TryGetValue(connId, out var conn);
            if (conn == null || conn != ni.Owner)
            {
                Debug.LogWarning( $"Conn {connId} tried Command '{info.Method.Name}' without authority.");
                return;
            }
        }

        script.DispatchRpc((ushort)rpcId, argSeg);
    }

    /// <summary>
    /// Spawn a prefab-based networked object on the server and all clients.
    /// </summary>
    public NetworkIdentity Spawn(Prefab prefab, Vector3 pos, Quaternion rot, NetworkConnection owner = null)
    {
        if (!prefabs.ContainsValue(prefab))
        {
            Debug.LogError("Prefab is not registered as a spawnable Prefab.");
            return null;
        }
        
        var actor = PrefabManager.SpawnPrefab(prefab, pos, rot);
        var ni = actor.GetScript<NetworkIdentity>();
        if (ni == null)
        {
            Debug.LogError("Prefab has no NetworkIdentity");
            Object.Destroy(actor);
            return null;
        }

        ni.AssetId = prefab.ID;
        ni.NetId = _nextNetId++;
        ni.Owner = owner;
        Spawned[ni.NetId] = ni;
        
        foreach(var s in ni.Scripts) s.OnNetworkSpawn();

        RebuildObservers();
        return ni;
    }

    public void Despawn(NetworkIdentity ni)
    {
        if (ni == null || !Spawned.ContainsKey(ni.NetId)) return;
        
        _w.Reset();
        _w.BeginMessage(MsgType.Despawn);
        _w.WriteUIntVar(ni.NetId);
        _w.FinishMessage();
        foreach (var conn in Connections.Values.Where(conn => conn.Observing.Remove(ni.NetId)))
            transport.ServerSend(conn.Id, _w.ToSegment());
        
        foreach(var s in ni.Scripts) s.OnNetworkDespawn();
        Spawned.Remove(ni.NetId);
        if (ni.SceneId != 0)
            ni.Actor.IsActive = false;
        else
            Object.Destroy(ni.Actor);
    }

    private void SendSpawn(NetworkConnection conn, NetworkIdentity ni)
    {
        // Existing observers need pending operations before the new observer's snapshot.
        SendState(ni);
        _w.Reset();
        _w.BeginMessage(MsgType.Spawn);
        _w.WriteUIntVar(ni.NetId);
        _w.WriteGuid(ni.AssetId);
        _w.WriteULongVar(ni.SceneId);
        var p = ni.Actor.Position;
        _w.Write(new Float3(p.X, p.Y, p.Z));
        _w.Write(ni.Actor.Orientation);
        _w.WriteByte((byte)(conn == ni.Owner ? 1 : 0));
        if (NetworkManager.IsHost) ni.IsOwnedLocally = conn == ni.Owner;

        var scripts = ni.Scripts;
        _w.WriteByte((byte)scripts.Length);
        foreach (var script in scripts)
            script.SerializeFull(_w);
        _w.FinishMessage();
        
        transport.ServerSend(conn.Id, _w.ToSegment());
        conn.Observing.Add(ni.NetId);
    }
    
    /// <summary>
    /// Called each tick: push dirty syncvars to clients.
    /// </summary>
    public void Update()
    {
        if (_pendingConns.Count > 0 || _rebuildTimer.ElapsedMilliseconds >= RebuildInterval * 1000)
        {
            RebuildObservers();
            _rebuildTimer.Restart();
        }
        
        foreach (var ni in Spawned.Values)
            SendState(ni);
        
        while (_pendingConns.Count > 0)
        {
            var conn = _pendingConns.Dequeue();
            if (!Connections.ContainsKey(conn.Id))
                continue;
            _w.Reset();
            _w.BeginMessage(MsgType.WelcomeDone);
            _w.FinishMessage();
            transport.ServerSend(conn.Id, _w.ToSegment());
            
            OnPlayerLoaded?.Invoke(conn);
        }
    }
    
    private void SendState(NetworkIdentity ni)
    {
        foreach (var script in ni.Scripts)
        {
            if (!script.AnyDirty()) continue;

            _w.Reset();
            _w.BeginMessage(MsgType.State);
            _w.WriteUIntVar(ni.NetId);
            _w.WriteByte(script.ComponentIndex);
            script.SerializeDelta(_w);
            _w.FinishMessage();

            SendToObservers(ni, _w.ToSegment());
        }
    }

    private readonly List<NetworkIdentity> _allCache = [];
    private readonly HashSet<uint> _desired = [];
    private readonly List<uint> _toAdd = [];
    private readonly List<uint> _toRemove = [];
    public void RebuildObservers()
    {
        _allCache.Clear();
        _allCache.AddRange(Spawned.Values);
        Interest.Rebuild(_allCache);

        foreach (var conn in Connections.Values)
        {
            _desired.Clear();
            Interest.GatherVisible(conn, _desired);
            
            _toAdd.Clear();
            foreach (var netId in _desired.Where(netId => !conn.Observing.Contains(netId)))
                _toAdd.Add(netId);
            
            _toRemove.Clear();
            foreach(var netId in conn.Observing.Where(netId => !_desired.Contains(netId)))
                _toRemove.Add(netId);
            
            foreach(var netId in _toAdd)
                if(Spawned.TryGetValue(netId, out var ni))
                    SendSpawn(conn, ni);

            foreach (var netId in _toRemove)
            {
                SendHide(conn, netId);
                conn.Observing.Remove(netId);
            }
        }
    }

    private void SendHide(NetworkConnection conn, NetworkIdentity ni) => SendHide(conn, ni.NetId);
    private void SendHide(NetworkConnection conn, uint netId)
    {
        _w.Reset();
        _w.BeginMessage(MsgType.Despawn);
        _w.WriteUIntVar(netId);
        _w.FinishMessage();
        transport.ServerSend(conn.Id, _w.ToSegment());  
    }
}
