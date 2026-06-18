using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlaxEngine;
using FlaxEngine.Networking;

namespace Reflect;

public enum RpcKind : byte
{
    Command,
    ClientRpc,
    TargetRpc
}

public sealed class RpcInfo
{
    public ushort Id;
    public MethodInfo Method;
    public ParameterInfo[] Params;
    public RpcKind Kind;
    public bool RequiresAuthority;
    public NetworkChannelType ChannelType;
}

public static class RpcRegistry
{
    private static readonly Dictionary<Type, Dictionary<ushort, RpcInfo>> ById = [];
    private static readonly Dictionary<Type, Dictionary<string, RpcInfo>> ByName = [];

    public static Dictionary<ushort, RpcInfo> Get(Type t)
    {
        if (!ById.ContainsKey(t)) Build(t);
        return ById[t];
    }

    public static RpcInfo GetByName(Type t, string name)
    {
        if (!ByName.ContainsKey(t)) Build(t);
        return !ByName[t].TryGetValue(name, out var info) ? throw new InvalidOperationException($"{t.Name} has no RPC '{name}'") : info;
    }

    private static void Build(Type t)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public  | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var methods = new List<MethodInfo>();
        for(var cur = t; cur != null && cur != typeof(NetworkScript); cur = cur.BaseType)
            methods.AddRange(cur.GetMethods(flags));

        var rpcs = methods
            .Where(m => m.IsDefined(typeof(CommandAttribute)) || m.IsDefined(typeof(ClientRpcAttribute)) || m.IsDefined(typeof(TargetRpcAttribute)))
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
        
        var byId = new Dictionary<ushort, RpcInfo>();
        var byName = new Dictionary<string, RpcInfo>();

        ushort id = 0;
        foreach (var m in rpcs)
        {
            var cmd = m.GetCustomAttribute<CommandAttribute>();
            var kind = cmd != null ? RpcKind.Command :
                m.IsDefined(typeof(ClientRpcAttribute)) ? RpcKind.ClientRpc : RpcKind.TargetRpc;

            var info = new RpcInfo
            {
                Id = id,
                Method = m,
                Params = m.GetParameters(),
                Kind = kind,
                RequiresAuthority = cmd?.RequiresAuthority ?? false,
                ChannelType = cmd?.ChannelType ?? m.GetCustomAttribute<ClientRpcAttribute>()?.ChannelType ?? m.GetCustomAttribute<TargetRpcAttribute>()?.ChannelType ?? NetworkChannelType.ReliableOrdered,
            };

            byId[id] = info;
            byName[m.Name] = info;
            id++;
        }

        ById[t] = byId;
        ByName[t] = byName;
    }
}

public static class RpcArgs
{
    public static void Write(NetworkWriter outer, RpcInfo info, object[] args)
    {
        if(args.Length != info.Params.Length)
            throw new ArgumentException($"{info.Method.Name} expects {info.Params.Length} args, got {args.Length}");

        var scratch = new NetworkWriter();
        for(var i = 0; i < info.Params.Length; i++)
            Serializers.WriteBoxed(scratch, info.Params[i].ParameterType, args[i]);
            
        outer.WriteBytes(scratch.ToSegment());
    }
        
    public static object[] Parse(ArraySegment<byte> seg, RpcInfo info)
    {
        var r = new NetworkReader();
        r.SetBuffer(seg);
        var args = new object[info.Params.Length];
        for (var i = 0; i < info.Params.Length; i++)
            args[i] = Serializers.ReadBoxed(r, info.Params[i].ParameterType);
        return args;
    }
}