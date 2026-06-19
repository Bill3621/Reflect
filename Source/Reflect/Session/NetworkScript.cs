using System;
using System.Linq;
using System.Reflection;
using FlaxEngine;

namespace Reflect;

[RequireScript(typeof(NetworkIdentity))]
public abstract class NetworkScript : Script
{
    public NetworkIdentity Identity { get; internal set; }
    public byte ComponentIndex { get; internal set; }

    public bool IsServer => Identity != null && NetworkIdentity.IsServer;
    public bool IsClient => Identity != null && NetworkIdentity.IsClient;
    public bool HasAuthority => Identity != null && (NetworkIdentity.IsServer || Identity.IsOwnedLocally);

    private ISyncVar[] _syncVars;
    public ISyncVar[] SyncVars
    {
        get
        {
            if (_syncVars != null) return _syncVars;
            
            _syncVars = GetType()
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => typeof(ISyncVar).IsAssignableFrom(f.FieldType))
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f => (ISyncVar)f.GetValue(this))
                .ToArray();
                
            if(_syncVars.Length > 64)
                Debug.LogError($"{GetType().Name} has >64 SyncVars; mask is 64-bit.");

            return _syncVars;
        }
    }

    public bool AnyDirty()
    {
        var sv = SyncVars;
        for (int i = 0; i < sv.Length; i++)
            if (sv[i].IsDirty)
                return true;
        return false;
    }

    public void SerializeDelta(NetworkWriter w)
    {
        var sv = SyncVars;
        ulong mask = 0;
        for(var i = 0; i < sv.Length; i++)
            if (sv[i].IsDirty)
                mask |= 1UL << i;
        
        w.WriteULongVar(mask);
        for (var i = 0; i < sv.Length; i++)
        {
            if (!sv[i].IsDirty) continue;
            sv[i].SerializeDelta(w);
            sv[i].ClearDirty();
        }
    }

    public void SerializeFull(NetworkWriter w)
    {
        var sv = SyncVars;
        var mask = sv.Length == 64 ? ulong.MaxValue : (1UL << sv.Length) - 1;
        w.WriteULongVar(mask);
        for (var i = 0; i < sv.Length; i++)
            sv[i].SerializeFull(w);
    }

    public void Deserialize(NetworkReader r, bool initialState)
    {
        var sv = SyncVars;
        var mask = r.ReadULongVar();
        for (var i = 0; i < sv.Length; i++)
        {
            if ((mask & (1UL << i)) == 0) continue;
            
            if (initialState)
                sv[i].DeserializeFull(r);
            else
                sv[i].DeserializeDelta(r);
        }
    }

    protected void SendCommand(string methodName, params object[] args)
    {
        var nm = NetworkManager.Instance;
        if (nm == null || !NetworkManager.IsClient)
        {
            Debug.LogError("SendCommand requires an active client.");
            return;
        }

        var info = RpcRegistry.GetByName(GetType(), methodName);

        var w = new NetworkWriter();
        w.BeginMessage(MsgType.Command);
        w.WriteUIntVar(Identity.NetId);
        w.WriteByte(ComponentIndex);
        w.WriteUIntVar(info.Id);
        RpcArgs.Write(w, info, args);
        w.FinishMessage();

        nm.Client.Send(w.ToSegment(), info.ChannelType);
    }
    
    protected void SendClientRpc(string methodName, params object[] args)
    {
        var nm = NetworkManager.Instance;
        if (nm == null || !NetworkManager.IsServer)
        {
            Debug.LogError("SendClientRpc requires an active server.");
            return;
        }

        var info = RpcRegistry.GetByName(GetType(), methodName);

        var w = new NetworkWriter();
        w.BeginMessage(MsgType.ClientRpc);
        w.WriteUIntVar(Identity.NetId);
        w.WriteByte(ComponentIndex);
        w.WriteUIntVar(info.Id);
        RpcArgs.Write(w, info, args);
        w.FinishMessage();

        nm.Server.SendToObservers(Identity, w.ToSegment(), info.ChannelType);
    }
    
    protected void SendTargetRpc(NetworkConnection target, string methodName, params object[] args)
    {
        var nm = NetworkManager.Instance;
        if (nm == null || !NetworkManager.IsServer)
        {
            Debug.LogError("SendClientRpc requires an active server.");
            return;
        }

        var info = RpcRegistry.GetByName(GetType(), methodName);

        var w = new NetworkWriter();
        w.BeginMessage(MsgType.TargetRpc);
        w.WriteUIntVar(Identity.NetId);
        w.WriteByte(ComponentIndex);
        w.WriteUIntVar(info.Id);
        RpcArgs.Write(w, info, args);
        w.FinishMessage();

        nm.Server.SendToConnection(target, w.ToSegment(), info.ChannelType);
    }
    
    internal void DispatchRpc(ushort rpcId, ArraySegment<byte> argSeg)
    {
        var map = RpcRegistry.Get(GetType());
        if (!map.TryGetValue(rpcId, out var info))
        {
            Debug.LogError($"{GetType().Name}: unknown rpcId {rpcId}");
            return;
        }
        var args = RpcArgs.Parse(argSeg, info);
        info.Method.Invoke(this, args);
    }
    
    public virtual void OnNetworkSpawn() {}
    public virtual void OnNetworkDespawn() {}
}
