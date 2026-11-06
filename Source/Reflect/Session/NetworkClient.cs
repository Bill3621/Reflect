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
    private readonly NetworkReader _packetReader = new();
    private readonly NetworkReader _r = new();

    private readonly Dictionary<uint, NetworkIdentity> _spawned = [];
    private readonly List<SceneObjectState> _sceneStates = [];
    private NetworkServer _hostServer;
    public Dictionary<uint, NetworkIdentity> Spawned => _hostServer?.Spawned ?? _spawned;
    public readonly Dictionary<ulong, NetworkIdentity> SceneObjects = [];
    
    public bool Active { get; private set; }
    public event Action OnReady;

    public void Connect()
    {
        if (Active) return;
        Active = true;
        _hostServer = NetworkManager.IsServer ? NetworkManager.Instance.Server : null;
        transport.OnClientData += OnData;
        transport.OnClientDisconnected += OnDisconnected;
        try
        {
            if (_hostServer == null) RegisterSceneObjects();
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
        try
        {
            transport.ClientDisconnect();
        }
        finally
        {
            if (_hostServer != null)
                foreach (var identity in _hostServer.Spawned.Values)
                    identity.IsOwnedLocally = false;
            foreach (var identity in _spawned.Values.ToArray())
            {
                identity.NotifyDespawn();
                identity.ResetNetworkState();
                if (identity.SceneId == 0) Object.Destroy(identity.Actor);
            }
            _spawned.Clear();
            foreach (var state in _sceneStates) state.Restore();
            _sceneStates.Clear();
            SceneObjects.Clear();
            _hostServer = null;
            _packetReader.SetBuffer(default);
            _r.SetBuffer(default);
        }
    }

    private void RegisterSceneObjects()
    {
        foreach (var ni in Level.GetScripts<NetworkIdentity>().Where(x => x.SceneId != 0))
        {
            SceneObjects.Add(ni.SceneId, ni);
            _sceneStates.Add(new SceneObjectState(ni));
        }
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
                switch (type)
                {
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
        var sceneId = _r.ReadULongVar();
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
        if (sceneId != 0)
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
            if (sceneId == 0) Object.Destroy(ni.Actor);
            throw;
        }

        if (sceneId != 0) ni.Actor.IsActive = true;
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
        if (ni.SceneId != 0)
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
