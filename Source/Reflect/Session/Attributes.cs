using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.Networking;

namespace Reflect;

/// <summary>
/// Client -> server. Body runs on the server.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class CommandAttribute : Attribute
{
    public bool RequiresAuthority = true;
    public NetworkChannelType ChannelType = NetworkChannelType.ReliableOrdered;
}

/// <summary>
/// Server -> all observers. Body runs on clients.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ClientRpcAttribute : Attribute {
    public NetworkChannelType ChannelType = NetworkChannelType.ReliableOrdered;
}

/// <summary>
/// Server -> one connection. Body runs on that client.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TargetRpcAttribute : Attribute {
    public NetworkChannelType ChannelType = NetworkChannelType.ReliableOrdered;
}