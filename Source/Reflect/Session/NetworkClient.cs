using System;
using System.Collections.Generic;
using System.Linq;
using FlaxEngine;
using FlaxEngine.Networking;
using Object = FlaxEngine.Object;

namespace Reflect;

public sealed class NetworkClient(ITransport transport, Dictionary<Guid, Prefab> prefabs)
{

    private readonly ITransport _transport = transport;
    private readonly Dictionary<Guid, Prefab> _prefabs = prefabs;
    private readonly NetworkReader _r = new();

    public readonly Dictionary<uint, NetworkIdentity> Spawned = [];
    public readonly Dictionary<ulong, NetworkIdentity> SceneObjects = [];
    
    public bool Active { get; private set; }
    public event Action OnReady;

    public void Connect()
    {
        _transport.OnClientData += OnData;
        _transport.OnClientDisconnected += OnDisconnected;
        _transport.ClientConnect();
        Active = true;
        RegisterSceneObjects();
    }

    public void Disconnect()
    {
        _transport.OnClientData -= OnData;
        _transport.OnClientDisconnected -= OnDisconnected;
        _transport.ClientDisconnect();
        Active = false;
    }

    private void RegisterSceneObjects()
    {
        foreach (var ni in Level.GetScripts<NetworkIdentity>().Where(x => x.SceneId != 0))
        {
            SceneObjects.Add(ni.SceneId, ni);
        }
    }

    private void OnDisconnected() => Disconnect();
    
    public void Send(ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered) => _transport.ClientSend(data, channelType);
    
    private void OnData(ArraySegment<byte> data)
    {
        _r.SetBuffer(data);
        while (_r.HasMore)
        {
            var type = Msg.ReadHeader(_r);
            var msgLen = _r.ReadLength();
            var msgEnd = _r.Position + msgLen;
            switch (type)
            {
                case MsgType.Spawn: HandleSpawn(); break;
                case MsgType.Despawn: HandleDespawn(); break;
                case MsgType.State: HandleState(msgEnd); break;
                case MsgType.WelcomeDone: OnReady?.Invoke(); break;
                case MsgType.ClientRpc:
                case MsgType.TargetRpc: HandleRpc(); break;
                case MsgType.Command:
                default: _r.SkipRemaining(msgEnd); break;
            }
            _r.SkipRemaining(msgEnd);
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

        if (sceneId != 0 && SceneObjects.TryGetValue(sceneId, out var ni))
        {
            ni.Actor.IsActive = true;
        }
        else
        {
            if (!_prefabs.TryGetValue(assetId, out var prefab))
            {
                Debug.LogError($"No prefab registered for {assetId}");
                return;
            }

            var actor = PrefabManager.SpawnPrefab(prefab, new Vector3(pos.X, pos.Y, pos.Z), rot);
            ni = actor.GetScript<NetworkIdentity>();
        }

        ni.NetId = netId;
        ni.AssetId = assetId;
        ni.SceneId = sceneId;
        ni.IsOwnedLocally = isOwner;
        Spawned[netId] = ni;
        
        var count = _r.ReadByte();
        var scripts = ni.Scripts;
        for (var i = 0; i < count; i++)
            scripts[i].Deserialize(_r);

        foreach (var s in scripts) s.OnNetworkSpawn();
    }

    private void HandleDespawn()
    {
        var netId = _r.ReadUIntVar();
        if (!Spawned.TryGetValue(netId, out var ni)) return;
        foreach (var s in ni.Scripts) s.OnNetworkDespawn();
        Spawned.Remove(netId);
        Object.Destroy(ni.Actor);
    }

    private void HandleState(int msgEnd)
    {
        var netId = _r.ReadUIntVar();
        var compIndex = _r.ReadByte();
        if (Spawned.TryGetValue(netId, out var ni))
            ni.Scripts[compIndex].Deserialize(_r);
        else
        {
            _r.SkipRemaining(msgEnd);
            Debug.LogWarning($"State for unknown netId {netId}, skipped");
        }
    }

    private void HandleRpc()
    {
        var netId = _r.ReadUIntVar();
        var comp = _r.ReadByte();
        var rpcId = (ushort)_r.ReadUIntVar();
        var argSeg = _r.ReadBytes();
        
        if(Spawned.TryGetValue(netId, out var ni))
            ni.Scripts[comp] .DispatchRpc(rpcId, argSeg);
        else
            Debug.LogWarning($"RPC for unknown netId {netId} (skipped)");
    }
}
