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
    
    private readonly List<NetworkConnection> _promoted = [];
    private readonly List<SceneObjectState> _sceneStates = [];

    public readonly Dictionary<uint, NetworkIdentity> Spawned = [];
    public readonly Dictionary<int, NetworkConnection> Connections = [];

    public IInterestManagement Interest = new GlobalInterest();
    private readonly Stopwatch _rebuildTimer = Stopwatch.StartNew();
    public float RebuildInterval = 1.0f;
    
    private uint _nextNetId = 1;
    private int _nextBotId = -1;
    
    public bool Active { get; private set; }

    /// <summary>The scene every client is told to load. Changed with <see cref="ChangeScene"/>.</summary>
    public SceneReference ActiveScene { get; private set; }
    /// <summary>True between <see cref="ChangeScene"/> and the server finishing its own load.</summary>
    public bool IsLoadingScene { get; private set; }
    private uint _loadId;

    /// <summary>Fires on the server once a scene change finished and its scene objects are registered.</summary>
    public event Action<SceneReference> OnSceneLoaded;
    public event Action<NetworkConnection> OnPlayerConnected;
    /// <summary>Fires once per connection per scene, after that connection received the scene's world.</summary>
    public event Action<NetworkConnection> OnPlayerLoaded;
    #nullable enable
    public event Action<NetworkConnection?>? OnPlayerDisconnected;
    #nullable disable

    public void Start()
    {
        if (Active) return;
        if (Level.ScenesCount == 0)
            throw new InvalidOperationException("Load a scene before starting the server.");
        Active = true;
        ActiveScene = new SceneReference(Level.Scenes[0].ID);
        _loadId = 1;
        transport.OnServerConnected += OnConnected;
        transport.OnServerData += OnData;
        transport.OnServerDisconnected += OnDisconnected;
        Level.SceneLoaded += OnLevelSceneLoaded;
        try
        {
            transport.ServerStart();
            RegisterSceneObjects();
            _rebuildTimer.Restart();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    private void RegisterSceneObjects()
    {
        foreach (var ni in Level.GetScripts<NetworkIdentity>())
        {
            ni.SceneId = ni.ID;
            _sceneStates.Add(new SceneObjectState(ni));
            ni.NetId = _nextNetId++;
            Spawned[ni.NetId] = ni;
            foreach (var s in ni.Scripts) s.OnNetworkSpawn();
        }
    }

    public void Stop()
    {
        if (!Active) return;
        Active = false;
        transport.OnServerConnected -= OnConnected;
        transport.OnServerData -= OnData;
        transport.OnServerDisconnected -= OnDisconnected;
        Level.SceneLoaded -= OnLevelSceneLoaded;
        try { transport.ServerStop(); }
        finally { ClearSession(); }
    }

    private void ClearSession()
    {
        foreach (var connection in Connections.Values)
        {
            connection.Observing.Clear();
            connection.OwnedPlayer = null;
        }
        Connections.Clear();
        _promoted.Clear();
        foreach (var identity in Spawned.Values.ToArray()) Despawn(identity);
        foreach (var state in _sceneStates) state.Restore();
        _sceneStates.Clear();
        Interest.Rebuild(Array.Empty<NetworkIdentity>());
        ActiveScene = default;
        IsLoadingScene = false;
        _loadId = 0;
        _nextBotId = -1;
        _allCache.Clear();
        _desired.Clear();
        _toAdd.Clear();
        _toRemove.Clear();
        _rebuildTimer.Reset();
        _packetReader.SetBuffer(default);
        _r.SetBuffer(default);
    }

    /// <summary>
    /// Moves the whole session to another scene. The server drops its world, tells every client to load the scene,
    /// and loads it locally. Each client then rejoins like a new player: spawns, WelcomeDone, OnPlayerLoaded.
    /// </summary>
    public void ChangeScene(SceneReference scene)
    {
        if (!Active)
        {
            Debug.LogError("ChangeScene requires an active server.");
            return;
        }
        if (IsLoadingScene)
        {
            Debug.LogWarning($"ChangeScene ignored, still loading {ActiveScene.ID}.");
            return;
        }
        if (!Content.GetAssetInfo(scene.ID, out _))
        {
            Debug.LogError($"ChangeScene: no scene asset with ID {scene.ID}.");
            return;
        }
        
        foreach (var ni in Spawned.Values.ToArray())
        {
            ni.NotifyDespawn();
            ni.ResetNetworkState();
            if (!ni.IsSceneObject) Object.Destroy(ni.Actor);
        }
        Spawned.Clear();
        _sceneStates.Clear();
        Interest.Rebuild(Array.Empty<NetworkIdentity>());

        _loadId++;
        ActiveScene = scene;
        IsLoadingScene = true;
        foreach (var conn in Connections.Values)
        {
            conn.Observing.Clear();
            conn.OwnedPlayer = null;
            conn.Phase = ConnectionPhase.LoadingScene;
            SendLoadScene(conn);
        }

        // Flax returns true on failure.
        if (Level.ChangeSceneAsync(scene.ID))
            Debug.LogError($"ChangeScene: Level refused to load {scene.ID}.");
    }

    private void OnLevelSceneLoaded(Scene scene, Guid sceneId)
    {
        if (!IsLoadingScene || sceneId != ActiveScene.ID) return;
        IsLoadingScene = false;
        RegisterSceneObjects();
        _rebuildTimer.Restart();
        OnSceneLoaded?.Invoke(ActiveScene);
    }

    private void SendLoadScene(NetworkConnection conn)
    {
        _w.Reset();
        _w.BeginMessage(MsgType.LoadScene);
        _w.WriteGuid(ActiveScene.ID);
        _w.WriteUIntVar(_loadId);
        _w.FinishMessage();
        Send(conn, _w.ToSegment());
        if (conn.IsBot) conn.ReadyLoadId = _loadId;
    }

    private void OnConnected(int connId)
    {
        Debug.Log($"Client connected with connId={connId}");
        Accept(new NetworkConnection { Id = connId, Phase = ConnectionPhase.LoadingScene });
    }

    private void Accept(NetworkConnection conn)
    {
        Connections[conn.Id] = conn;
        SendLoadScene(conn);
        OnPlayerConnected?.Invoke(conn);
    }

    public NetworkConnection AddBot()
    {
        if (!Active)
        {
            Debug.LogError("AddBot requires an active server.");
            return null;
        }
        var bot = new NetworkConnection { Id = _nextBotId--, IsBot = true, Phase = ConnectionPhase.LoadingScene };
        Debug.Log($"Bot added with connId={bot.Id}");
        Accept(bot);
        return bot;
    }
    
    public void RemoveBot(NetworkConnection bot)
    {
        if (bot is { IsBot: true }) OnDisconnected(bot.Id);
    }
    
    private void Send(NetworkConnection conn, ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered)
    {
        if (!conn.IsBot) transport.ServerSend(conn.Id, data, channelType);
    }

    /// <summary>
    /// Disconnects one client. The client receives <paramref name="message"/> and reports <see cref="DisconnectReason.Kicked"/>.
    /// </summary>
    public void Kick(NetworkConnection conn, string message = "")
    {
        if (!Active || conn == null || !Connections.ContainsKey(conn.Id)) return;
        _w.Reset();
        _w.BeginMessage(MsgType.Disconnect);
        _w.WriteByte((byte)DisconnectReason.Kicked);
        _w.WriteString(message);
        _w.FinishMessage();
        try
        {
            // Sent directly, since SendToConnection skips clients that are still loading.
            Send(conn, _w.ToSegment());
        }
        finally
        {
            // The transport delivers the message before it drops the connection.
            if (!conn.IsBot) transport.ServerDisconnect(conn.Id);
            OnDisconnected(conn.Id);
        }
    }

    private void OnDisconnected(int connId)
    {
        if (!Connections.Remove(connId, out var conn)) return;
        Debug.Log($"Client disconnected with connId={connId}");
        if (conn.OwnedPlayer != null) Despawn(conn.OwnedPlayer);
        conn.Observing.Clear();
        conn.OwnedPlayer = null;
        OnPlayerDisconnected?.Invoke(conn);
    }

    public void SendToObservers(NetworkIdentity ni, ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered)
    {
        foreach (var conn in Connections.Values.Where(conn => conn.Observing.Contains(ni.NetId)))
            Send(conn, data, channelType);
    }

    public void SendToConnection(NetworkConnection conn, ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered)
    {
        // A loading client has no objects yet, so anything addressed to one would be dropped anyway.
        if (conn is { Phase: not ConnectionPhase.LoadingScene })
            Send(conn, data, channelType);
    }
    
    private void OnData(int connId, ArraySegment<byte> data)
    {
        if (!Connections.TryGetValue(connId, out var conn)) return;
        _packetReader.SetBuffer(data);
        try
        {
            while (_packetReader.HasMore)
            {
                var type = Msg.ReadHeader(_packetReader);
                _r.SetBuffer(_packetReader.ReadSegment(_packetReader.ReadLength()));
                switch (type)
                {
                    case MsgType.Command: HandleCommand(conn); break;
                    case MsgType.SceneReady: HandleSceneReady(conn); break;
                    default: Debug.LogWarning($"Server got unexpected msg {type}"); break;
                }
            }
        }
        catch (InvalidDataException ex)
        {
            Debug.LogWarning($"Dropped invalid packet from conn {connId}: {ex.Message}");
        }
    }
    
    private void HandleSceneReady(NetworkConnection conn)
    {
        var loadId = _r.ReadUIntVar();
        if (_r.HasMore)
            throw new InvalidDataException("SceneReady has trailing data.");
        if (conn.Phase == ConnectionPhase.LoadingScene && loadId == _loadId)
            conn.ReadyLoadId = loadId;
    }

    private void HandleCommand(NetworkConnection conn)
    {
        var connId = conn.Id;
        var netId = _r.ReadUIntVar();
        var comp = _r.ReadByte();
        var rpcId = _r.ReadUIntVar();
        var argSeg = _r.ReadBytes();
        if (_r.HasMore)
            throw new InvalidDataException("Command has trailing data.");
        if (conn.Phase == ConnectionPhase.LoadingScene) return;

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
            if (conn != ni.Owner)
            {
                Debug.LogWarning( $"Conn {connId} tried Command '{info.Method.Name}' without authority.");
                return;
            }
        }

        script.DispatchRpc((ushort)rpcId, argSeg);
    }

    /// <summary>
    /// Instantiates a prefab on the server and spawns it on every connection whose interest includes it.
    /// </summary>
    public NetworkIdentity Spawn(Prefab prefab, Vector3 pos, Quaternion rot, NetworkConnection owner = null)
    {
        if (!Active)
        {
            Debug.LogError("Spawn requires an active server.");
            return null;
        }
        if (IsLoadingScene)
        {
            Debug.LogError("Spawn is not allowed while the server is loading a scene. Wait for OnSceneLoaded.");
            return null;
        }
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
        if (ni == null || !Spawned.Remove(ni.NetId)) return;
        
        _w.Reset();
        _w.BeginMessage(MsgType.Despawn);
        _w.WriteUIntVar(ni.NetId);
        _w.FinishMessage();
        foreach (var conn in Connections.Values.Where(conn => conn.Observing.Remove(ni.NetId)))
            Send(conn, _w.ToSegment());
        
        ni.NotifyDespawn();
        ni.ResetNetworkState();
        if (ni.IsSceneObject)
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
        _w.WriteGuid(ni.SceneId);
        var p = ni.Actor.Position;
        _w.Write(new Float3(p.X, p.Y, p.Z));
        _w.Write(ni.Actor.Orientation);
        _w.WriteByte((byte)(conn == ni.Owner ? 1 : 0));

        var scripts = ni.Scripts;
        _w.WriteByte((byte)scripts.Length);
        foreach (var script in scripts)
            script.SerializeFull(_w);
        _w.FinishMessage();
        
        Send(conn, _w.ToSegment());
        conn.Observing.Add(ni.NetId);
    }
    
    /// <summary>
    /// Runs every sync interval. Moves scene-ready connections forward, rebuilds observers when due, and sends dirty SyncVars.
    /// </summary>
    public void Update()
    {
        if (!Active || IsLoadingScene) return;
        
        _promoted.Clear();
        foreach (var conn in Connections.Values)
        {
            if (conn.Phase != ConnectionPhase.LoadingScene || conn.ReadyLoadId != _loadId) continue;
            conn.Phase = ConnectionPhase.Syncing;
            _promoted.Add(conn);
        }

        if (_promoted.Count > 0 || _rebuildTimer.ElapsedMilliseconds >= RebuildInterval * 1000)
        {
            RebuildObservers();
            _rebuildTimer.Restart();
        }
        
        foreach (var ni in Spawned.Values)
            SendState(ni);
        
        foreach (var conn in _promoted)
        {
            // A handler below may have started another scene change or the conn may have dropped.
            if (conn.Phase != ConnectionPhase.Syncing || !Connections.ContainsKey(conn.Id)) continue;
            conn.Phase = ConnectionPhase.InGame;
            _w.Reset();
            _w.BeginMessage(MsgType.WelcomeDone);
            _w.FinishMessage();
            Send(conn, _w.ToSegment());
            
            OnPlayerLoaded?.Invoke(conn);
        }
        _promoted.Clear();
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
        if (!Active) return;
        _allCache.Clear();
        _allCache.AddRange(Spawned.Values);
        Interest.Rebuild(_allCache);

        foreach (var conn in Connections.Values)
        {
            if (conn.Phase == ConnectionPhase.LoadingScene || conn.IsBot) continue;
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
        Send(conn, _w.ToSegment());
    }
}
