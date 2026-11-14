using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.Networking;

namespace Reflect;

/// <summary>
/// Where a connection is in joining the server's current scene.
/// Authenticating: held by <see cref="NetworkServer.Authenticator"/>, not yet in <see cref="NetworkServer.Connections"/>.
/// LoadingScene: told to load, waiting for its SceneReady. Syncing: receiving spawns this tick. InGame: WelcomeDone sent.
/// </summary>
public enum ConnectionPhase : byte { Authenticating, LoadingScene, Syncing, InGame }

public sealed class NetworkConnection
{
    public int Id;
    public bool IsBot { get; internal init; }
    public ConnectionPhase Phase { get; internal set; }
    internal uint ReadyLoadId;
    internal long AuthStarted;
    public object AuthData;
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
    void ServerDisconnect(int connectionId);
    void ServerStop();
    
    // CLIENT
    event Action OnClientConnected;
    event Action<ArraySegment<byte>> OnClientData;
    /// <summary>The connection closed without the client asking. The argument is true when it timed out.</summary>
    event Action<bool> OnClientDisconnected;
    void ClientConnect();
    void ClientSend(ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered);
    void ClientDisconnect();

    void Poll();
}

/// <summary>
/// In-process transport with one simulated connection (id 1). No sockets, so tests can run server and client in one process.
/// </summary>
public sealed class LoopbackTransport : ITransport
{
    public event Action<int> OnServerConnected;
    public event Action<int, ArraySegment<byte>> OnServerData;
    public event Action<int> OnServerDisconnected;
    public event Action OnClientConnected;
    public event Action<ArraySegment<byte>> OnClientData;
    public event Action<bool> OnClientDisconnected;

    private readonly Queue<byte[]> _toServer = new Queue<byte[]>();
    private readonly Queue<byte[]> _toClient = new Queue<byte[]>();
    private bool _serverUp, _clientUp;
    private bool _dropClient;
    private const int ClientConnId = 1;

    public void ServerStart() => _serverUp = true;
    public void ServerStop()
    {
        _serverUp = false;
        _toServer.Clear();
        _toClient.Clear();
        _dropClient = false;
        if (!_clientUp) return;
        _clientUp = false;
        OnClientDisconnected?.Invoke(false);
    }

    public void ServerSend(int connectionId, ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered)
    {
        if (_serverUp && _clientUp && !_dropClient && connectionId == ClientConnId)
            _toClient.Enqueue(data.ToArray());
    }

    public void ServerDisconnect(int connectionId)
    {
        if (_clientUp && connectionId == ClientConnId) _dropClient = true;
    }

    public void ClientConnect()
    {
        if (_clientUp) return;
        if (!_serverUp) throw new InvalidOperationException("Loopback requires a running server.");
        _clientUp = true;
        _dropClient = false;
        OnClientConnected?.Invoke();
        OnServerConnected?.Invoke(ClientConnId);
    }

    public void ClientSend(ArraySegment<byte> data, NetworkChannelType channelType = NetworkChannelType.ReliableOrdered)
    {
        if (_serverUp && _clientUp) _toServer.Enqueue(data.ToArray());
    }

    public void ClientDisconnect()
    {
        if (!_clientUp) return;
        _clientUp = false;
        _dropClient = false;
        _toServer.Clear();
        _toClient.Clear();
        if (_serverUp) OnServerDisconnected?.Invoke(ClientConnId);
    }

    public void Poll()
    {
        while (_toServer.Count > 0)
            OnServerData?.Invoke(ClientConnId, new ArraySegment<byte>(_toServer.Dequeue()));
        while (_toClient.Count > 0)
            OnClientData?.Invoke(new ArraySegment<byte>(_toClient.Dequeue()));
        if (!_dropClient) return;
        _dropClient = false;
        if (!_clientUp) return;
        _clientUp = false;
        _toServer.Clear();
        OnClientDisconnected?.Invoke(false);
    }
}