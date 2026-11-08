using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlaxEngine;
using FlaxEngine.Networking;
using Object = FlaxEngine.Object;

namespace Reflect;

public sealed class NetworkClient(ITransport transport, Dictionary<Guid, Prefab> prefabs)
{
    private readonly NetworkWriter _w = new();
    private readonly NetworkReader _packetReader = new();
    private readonly NetworkReader _r = new();

    private readonly Dictionary<uint, NetworkIdentity> _spawned = [];
    private readonly List<SceneObjectState> _sceneStates = [];
    private NetworkServer _hostServer;
    public Dictionary<uint, NetworkIdentity> Spawned => _hostServer?.Spawned ?? _spawned;
    public readonly Dictionary<Guid, NetworkIdentity> SceneObjects = [];
    
    public bool Active { get; private set; }

    /// <summary>The scene the server told this client to be in.</summary>
    public SceneReference ActiveScene { get; private set; }
    public bool IsLoadingScene => _loadingScene != Guid.Empty;
    private Guid _loadingScene;
    private uint _loadId;

    /// <summary>Fires when the server moves this client to a scene. The old world is already torn down.</summary>
    public event Action<SceneReference> OnSceneChanging;
    /// <summary>Fires once per scene, after the server sent that scene's world.</summary>
    public event Action OnReady;

    public void Connect()
    {
        if (Active) return;
        Active = true;
        _hostServer = NetworkManager.IsServer ? NetworkManager.Instance.Server : null;
        transport.OnClientData += OnData;
        transport.OnClientDisconnected += OnDisconnected;
        Level.SceneLoaded += OnLevelSceneLoaded;
        try
        {
            transport.ClientConnect();
        }
        catch
        {
            Disconnect();
            throw;
        }
    }

    public void Disconnect()
    {
        if (!Active) return;
        Active = false;
        transport.OnClientData -= OnData;
        transport.OnClientDisconnected -= OnDisconnected;
        Level.SceneLoaded -= OnLevelSceneLoaded;
        try
        {
            transport.ClientDisconnect();
        }
        finally
        {
            if (_hostServer != null)
                foreach (var identity in _hostServer.Spawned.Values)
                    identity.IsOwnedLocally = false;
            DespawnAll();
            foreach (var state in _sceneStates) state.Restore();
            _sceneStates.Clear();
            SceneObjects.Clear();
            _hostServer = null;
            ActiveScene = default;
            _loadingScene = Guid.Empty;
            _loadId = 0;
            _packetReader.SetBuffer(default);
            _r.SetBuffer(default);
        }
    }
    
    private void DespawnAll()
    {
        foreach (var identity in _spawned.Values.ToArray())
        {
            identity.NotifyDespawn();
            identity.ResetNetworkState();
            if (!identity.IsSceneObject) Object.Destroy(identity.Actor);
        }
        _spawned.Clear();
    }

    private void RegisterSceneObjects()
    {
        foreach (var ni in Level.GetScripts<NetworkIdentity>())
        {
            ni.SceneId = ni.ID;
            SceneObjects.Add(ni.SceneId, ni);
            _sceneStates.Add(new SceneObjectState(ni));
        }
    }

    private void HandleLoadScene()
    {
        var scene = _r.ReadGuid();
        var loadId = _r.ReadUIntVar();
        if (_r.HasMore)
            throw new InvalidDataException("LoadScene has trailing data.");

        // The first load may target the scene this client already has open (editor play, same start scene).
        var firstLoad = _loadId == 0;
        DespawnAll();
        _sceneStates.Clear();
        SceneObjects.Clear();
        _loadId = loadId;
        ActiveScene = new SceneReference(scene);
        _loadingScene = Guid.Empty;
        OnSceneChanging?.Invoke(ActiveScene);

        // The host client shares the server's world, which the server loads itself.
        if (_hostServer != null)
        {
            SendSceneReady();
            return;
        }
        if (firstLoad && Level.ScenesCount == 1 && Level.Scenes[0].ID == scene)
        {
            FinishSceneLoad();
            return;
        }

        _loadingScene = scene;
        // Flax returns true on failure.
        if (Level.ChangeSceneAsync(scene))
        {
            Debug.LogError($"Server asked for scene {scene}, but it could not be loaded.");
            Disconnect();
        }
    }

    private void OnLevelSceneLoaded(Scene scene, Guid sceneId)
    {
        if (sceneId != _loadingScene) return;
        _loadingScene = Guid.Empty;
        FinishSceneLoad();
    }

    private void FinishSceneLoad()
    {
        RegisterSceneObjects();
        SendSceneReady();
    }

    private void SendSceneReady()
    {
        _w.Reset();
        _w.BeginMessage(MsgType.SceneReady);
        _w.WriteUIntVar(_loadId);
        _w.FinishMessage();
        Send(_w.ToSegment());
    }

    private void OnDisconnected() => Disconnect();
    
    public void Send(ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered) => transport.ClientSend(data, channelType);
    
    private void OnData(ArraySegment<byte> data)
    {
        _packetReader.SetBuffer(data);
        try
        {
            while (_packetReader.HasMore)
            {
                var type = Msg.ReadHeader(_packetReader);
                _r.SetBuffer(_packetReader.ReadSegment(_packetReader.ReadLength()));
                // Nothing from the previous scene applies while the next one loads.
                if (IsLoadingScene && type != MsgType.LoadScene) continue;
                switch (type)
                {
                    case MsgType.LoadScene: HandleLoadScene(); break;
                    case MsgType.Spawn: HandleSpawn(); break;
                    case MsgType.Despawn: HandleDespawn(); break;
                    case MsgType.State: HandleState(); break;
                    case MsgType.WelcomeDone: OnReady?.Invoke(); break;
                    case MsgType.ClientRpc:
                    case MsgType.TargetRpc: HandleRpc(type); break;
                }
            }
        }
        catch (InvalidDataException ex)
        {
            Debug.LogWarning($"Dropped invalid server packet: {ex.Message}");
        }
    }

    private void HandleSpawn()
    {
        var netId = _r.ReadUIntVar();
        var assetId = _r.ReadGuid();
        var sceneId = _r.ReadGuid();
        var pos = _r.Read<Float3>();
        var rot = _r.Read<Quaternion>();
        var isOwner = _r.ReadByte() == 1;
        var count = _r.ReadByte();

        if (_hostServer != null)
        {
            if (_hostServer.Spawned.TryGetValue(netId, out var shared))
                shared.IsOwnedLocally = isOwner;
            return;
        }

        if (Spawned.ContainsKey(netId))
        {
            Debug.LogWarning($"Tried spawning netId={netId}, but it was already spawned!");
            return;
        }

        NetworkIdentity ni;
        if (sceneId != Guid.Empty)
        {
            if (!SceneObjects.TryGetValue(sceneId, out ni))
            {
                Debug.LogError($"No scene object registered for {sceneId}");
                return;
            }
            ni.Actor.Position = new Vector3(pos.X, pos.Y, pos.Z);
            ni.Actor.Orientation = rot;
        }
        else
        {
            if (!prefabs.TryGetValue(assetId, out var prefab))
            {
                Debug.LogError($"No prefab registered for {assetId}");
                return;
            }

            var actor = PrefabManager.SpawnPrefab(prefab, new Vector3(pos.X, pos.Y, pos.Z), rot);
            ni = actor.GetScript<NetworkIdentity>();
        }

        var scripts = ni.Scripts;
        try
        {
            if (count != scripts.Length)
                throw new InvalidDataException($"Spawn component count {count} does not match {scripts.Length} local components.");
            ni.NetId = netId;
            ni.AssetId = assetId;
            ni.SceneId = sceneId;
            ni.IsOwnedLocally = isOwner;
            Spawned[netId] = ni;
            for (var i = 0; i < count; i++)
                scripts[i].Deserialize(_r, true);
            if (_r.HasMore)
                throw new InvalidDataException("Spawn has trailing state data.");
        }
        catch (InvalidDataException)
        {
            Spawned.Remove(netId);
            if (sceneId == Guid.Empty) Object.Destroy(ni.Actor);
            throw;
        }

        if (sceneId != Guid.Empty) ni.Actor.IsActive = true;
        foreach (var s in scripts) s.OnNetworkSpawn();
    }

    private void HandleDespawn()
    {
        if (_hostServer != null) return;
        var netId = _r.ReadUIntVar();
        if (!Spawned.TryGetValue(netId, out var ni)) return;
        ni.NotifyDespawn();
        Spawned.Remove(netId);
        ni.ResetNetworkState();
        if (ni.IsSceneObject)
            ni.Actor.IsActive = false;
        else
            Object.Destroy(ni.Actor);
    }

    private void HandleState()
    {
        if (_hostServer != null) return;
        var netId = _r.ReadUIntVar();
        var compIndex = _r.ReadByte();
        if (!Spawned.TryGetValue(netId, out var ni))
        {
            Debug.LogWarning($"State for unknown netId {netId}, skipped");
            return;
        }
        var scripts = ni.Scripts;
        if (compIndex >= scripts.Length)
            throw new InvalidDataException($"State has invalid component index {compIndex}.");
        scripts[compIndex].Deserialize(_r, false);
    }

    private void HandleRpc(MsgType type)
    {
        var netId = _r.ReadUIntVar();
        var comp = _r.ReadByte();
        var rpcId = _r.ReadUIntVar();
        var argSeg = _r.ReadBytes();
        if (_r.HasMore)
            throw new InvalidDataException("RPC has trailing data.");

        if (!Spawned.TryGetValue(netId, out var ni))
        {
            Debug.LogWarning($"RPC for unknown netId {netId} (skipped)");
            return;
        }
        var scripts = ni.Scripts;
        if (comp >= scripts.Length)
            throw new InvalidDataException($"RPC has invalid component index {comp}.");
        var script = scripts[comp];
        if (rpcId > ushort.MaxValue || !RpcRegistry.Get(script.GetType()).TryGetValue((ushort)rpcId, out var info))
            throw new InvalidDataException($"Unknown RPC ID {rpcId}.");
        var expectedKind = type == MsgType.ClientRpc ? RpcKind.ClientRpc : RpcKind.TargetRpc;
        if (info.Kind != expectedKind)
            throw new InvalidDataException($"RPC '{info.Method.Name}' does not match message type {type}.");
        script.DispatchRpc((ushort)rpcId, argSeg);
    }
}
