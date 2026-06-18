using System;
using System.Collections.Generic;
using FlaxEngine.Networking;

namespace Reflect;

public sealed class FlaxTransport : ITransport
{
    public event Action<int> OnServerConnected;
    public event Action<int, ArraySegment<byte>> OnServerData;
    public event Action<int> OnServerDisconnected;
    public event Action OnClientConnected;
    public event Action<ArraySegment<byte>> OnClientData;
    public event Action OnClientDisconnected;

    public string Address { get; set; } = "127.0.0.1";
    public ushort Port { get; set; } = 7777;
    public ushort MaxConnections { get; set; } = 32;
    public ushort MessageSize { get; set; } = 1500;
    public ushort MessagePoolSize { get; set; } = 2048;

    private NetworkPeer _serverPeer;
    private NetworkPeer _clientPeer;
    private readonly HashSet<uint> _serverConnections = [];
    
    public void ServerStart()
    {
        var config = CreateConfig(MaxConnections);
        _serverPeer = NetworkPeer.CreatePeer(config) ?? throw new InvalidOperationException("Failed to create server NetworkPeer.");
        if (_serverPeer.Listen()) return;
        NetworkPeer.ShutdownPeer(_serverPeer);
        _serverPeer = null;
        throw new InvalidOperationException("Failed to start server listener.");
    }

    public void ServerSend(int connectionId, ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered)
    {
        if (_serverPeer == null) return;
        
        var msg = _serverPeer.BeginSendMessage();
        WritePayload(ref msg, data);
        _serverPeer.EndSendMessage(channelType, msg,
            new FlaxEngine.Networking.NetworkConnection { ConnectionId = (uint)connectionId });
    }

    public void ServerStop()
    {
        if (_serverPeer == null) return;

        foreach (var id in _serverConnections)
            _serverPeer.Disconnect(new FlaxEngine.Networking.NetworkConnection() {ConnectionId = id});
        
        _serverConnections.Clear();
        NetworkPeer.ShutdownPeer(_serverPeer);
        _serverPeer = null;
    }
    
    public void ClientConnect()
    {
        var config = CreateConfig(1);
        _clientPeer = NetworkPeer.CreatePeer(config) ?? throw new InvalidOperationException("Failed to create client NetworkPeer.");
        if (_clientPeer.Connect()) return;
        NetworkPeer.ShutdownPeer(_clientPeer);
        _clientPeer = null;
        throw new InvalidOperationException("Failed to start client connection.");
    }

    public void ClientSend(ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered)
    {
        if (_clientPeer == null) return;

        var msg = _clientPeer.BeginSendMessage();
        WritePayload(ref msg, data);
        _clientPeer.EndSendMessage(channelType, msg);
    }
    
    public void ClientDisconnect()
    {
        if (_clientPeer == null) return;
        
        _clientPeer.Disconnect();
        NetworkPeer.ShutdownPeer(_clientPeer);
        _clientPeer = null;
    }

    public void Poll()
    {
        if (_serverPeer != null) PollPeer(_serverPeer, true);
        if (_clientPeer != null) PollPeer(_clientPeer, false);
    }

    private void PollPeer(NetworkPeer peer, bool isServer)
    {
        while (peer.PopEvent(out var evt))
        {
            switch (evt.EventType)
            {
                case NetworkEventType.Connected:
                    if (isServer)
                    {
                        _serverConnections.Add(evt.Sender.ConnectionId);
                        OnServerConnected?.Invoke((int)evt.Sender.ConnectionId);
                        
                    }
                    else
                        OnClientConnected?.Invoke();

                    break;
                case NetworkEventType.Disconnected:
                case NetworkEventType.Timeout:
                    if (isServer)
                    {
                        _serverConnections.Remove(evt.Sender.ConnectionId);
                        OnServerDisconnected?.Invoke((int)evt.Sender.ConnectionId);
                    }
                    else
                        OnClientDisconnected?.Invoke();
                    break;
                case NetworkEventType.Message:
                    var payload = ReadPayload(ref evt.Message);
                    if(isServer)
                        OnServerData?.Invoke((int)evt.Sender.ConnectionId, payload);
                    else
                        OnClientData?.Invoke(payload);
                    peer.RecycleMessage(evt.Message);
                    break;
            }
        }
    }

    private NetworkConfig CreateConfig(ushort maxConnections) => new()
    {
        NetworkDriver = new ENetDriver(),
        Address = Address,
        Port = Port,
        ConnectionsLimit = maxConnections,
        MessageSize = MessageSize,
        MessagePoolSize = MessagePoolSize
    };

    private void WritePayload(ref NetworkMessage msg, ArraySegment<byte> data)
    {
        if (data.Count > MessageSize - 4)
            throw new InvalidOperationException($"Payload {data.Count}B exceeds limit {MessageSize - 4}B.");
        
        msg.WriteInt32(data.Count);
        if (data.Count > 0)
            msg.WriteBytes(data.Array, data.Count);
    }

    private static ArraySegment<byte> ReadPayload(ref NetworkMessage msg)
    {
        var len = msg.ReadInt32();
        if (len <= 0) return default;
        var buf = new byte[len];
        msg.ReadBytes(buf, len);
        return new ArraySegment<byte>(buf);
    }
}
