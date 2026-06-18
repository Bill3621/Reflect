using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Reflect;

public sealed class NetworkConnection
{
    public int Id;
    public NetworkIdentity OwnedPlayer;
    public readonly HashSet<uint> Observing = [];
}

public interface ITransport
{
    // SERVER
    event Action<int> OnServerConnected;
    event Action<int, ArraySegment<byte>> OnServerData;
    event Action<int> OnServerDisconnected;
    void ServerStart();
    void ServerSend(int connectionId, ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered);
    void ServerStop();
    
    // CLIENT
    event Action OnClientConnected;
    event Action<ArraySegment<byte>> OnClientData;
    event Action OnClientDisconnected;
    void ClientConnect();
    void ClientSend(ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered);
    void ClientDisconnect();

    void Poll();
}

/// <summary>
/// In-process transport: server and client in the same app. Great for tests.
/// </summary>
public sealed class LoopbackTransport : ITransport
{
    public event Action<int> OnServerConnected;
    public event Action<int, ArraySegment<byte>> OnServerData;
    public event Action<int> OnServerDisconnected;
    public event Action OnClientConnected;
    public event Action<ArraySegment<byte>> OnClientData;
    public event Action OnClientDisconnected;

    private readonly Queue<byte[]> _toServer = new Queue<byte[]>();
    private readonly Queue<byte[]> _toClient = new Queue<byte[]>();
    private bool _serverUp, _clientUp;
    private const int ClientConnId = 1;

    public void ServerStart() => _serverUp = true;
    public void ServerStop() => _serverUp = false;

    public void ServerSend(int connectionId, ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered)
        => _toClient.Enqueue(data.ToArray());

    public void ClientConnect()
    {
        _clientUp = true;
        OnClientConnected?.Invoke();
        OnServerConnected?.Invoke(ClientConnId);
    }

    public void ClientSend(ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered) => _toServer.Enqueue(data.ToArray());

    public void ClientDisconnect()
    {
        if (!_clientUp) return;
        _clientUp = false;
        OnClientDisconnected?.Invoke();
        OnServerDisconnected?.Invoke(ClientConnId);
    }

    public void Poll()
    {
        while (_toServer.Count > 0)
            OnServerData?.Invoke(ClientConnId, new ArraySegment<byte>(_toServer.Dequeue()));
        while (_toClient.Count > 0)
            OnClientData?.Invoke(new ArraySegment<byte>(_toClient.Dequeue()));
    }
}