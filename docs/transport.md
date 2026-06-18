# Transport Layer

The transport is the lowest layer in Reflect. It moves bytes between a server and clients and tells the session layer when connections open, deliver data, or close. Everything above it (`NetworkServer`, `NetworkClient`, `NetworkManager`) is written against the `ITransport` interface, so the wire protocol can be swapped without touching gameplay code.

## ITransport

`ITransport` is the contract every transport implements. It has a server half and a client half.

```csharp
public interface ITransport
{
    // Server
    event Action<int> OnServerConnected;
    event Action<int, ArraySegment<byte>> OnServerData;
    event Action<int> OnServerDisconnected;
    void ServerStart();
    void ServerSend(int connectionId, ArraySegment<byte> data,
        NetworkChannelType channelType = NetworkChannelType.ReliableOrdered);
    void ServerStop();

    // Client
    event Action OnClientConnected;
    event Action<ArraySegment<byte>> OnClientData;
    event Action OnClientDisconnected;
    void ClientConnect();
    void ClientSend(ArraySegment<byte> data,
        NetworkChannelType channelType = NetworkChannelType.ReliableOrdered);
    void ClientDisconnect();

    void Poll();
}
```

Connections are identified by an `int` on the server side. Data arrives as an `ArraySegment<byte>`. The channel type is passed straight through to the underlying network layer.

`Poll` is the pump. `NetworkManager` calls it every frame. A transport is free to buffer events and deliver them all during `Poll`, which is exactly what `LoopbackTransport` does.

## FlaxTransport

`FlaxTransport` is the production implementation. It wraps Flax's `NetworkPeer` with an `ENetDriver`, which gives you ENet over UDP.

It exposes a handful of properties you set before starting:

- `Address` (string, default `127.0.0.1`)
- `Port` (ushort, default `7777`)
- `MaxConnections` (ushort, default `32`)
- `MessageSize` (ushort, default `1500`)
- `MessagePoolSize` (ushort, default `2048`)

`NetworkManager` constructs a `FlaxTransport` from its own `Address`, `Port`, and `MaxConnections` fields. If you build a transport yourself, set these before calling `ServerStart` or `ClientConnect`.

### NetworkConfig

Internally, `FlaxTransport` builds a Flax `NetworkConfig` and hands it to `NetworkPeer.CreatePeer`:

```csharp
var config = new NetworkConfig
{
    NetworkDriver = new ENetDriver(),
    Address = Address,
    Port = Port,
    ConnectionsLimit = maxConnections,
    MessageSize = MessageSize,
    MessagePoolSize = MessagePoolSize
};
```

`ConnectionsLimit` differs between server and client. The server uses `MaxConnections`. The client passes `1` since it only needs one outgoing connection.

`MessageSize` caps how large a single Flax `NetworkMessage` can be. `MessagePoolSize` controls the pool of reusable message buffers the peer draws from.

### Message pool lifecycle

Flax `NetworkPeer` uses a pool of `NetworkMessage` objects. When data arrives, `Poll` pops events off the peer and reads the message payload. After reading, it calls `peer.RecycleMessage(evt.Message)` to return the buffer to the pool.

This matters if you write your own transport or touch the peer directly. Messages are pooled objects, not owned by you. You must recycle them when you are done or the pool runs dry and the peer stops delivering.

### Length-prefixed payload

`FlaxTransport` adds its own framing on top of Reflect's message format. Flax's `NetworkMessage` is a flat buffer, so `FlaxTransport` writes the Reflect payload with a 4-byte length prefix:

```
[ int32 length ][ payload bytes ]
```

On send, `WritePayload` checks that the payload fits (`data.Count` must be under `MessageSize - 4`) and throws if it does not. On receive, `ReadPayload` reads the length, allocates a byte array, and copies the bytes out into an `ArraySegment<byte>`.

Because of this prefix, the practical max Reflect payload per send is `MessageSize - 4` bytes (1496 bytes with the default 1500 `MessageSize`). Keep your individual messages under that or raise `MessageSize`.

## LoopbackTransport

`LoopbackTransport` is the in-process implementation. It keeps two `Queue<byte[]>` (one server-bound, one client-bound) and drains them on `Poll`. There is exactly one simulated connection with ID `1`.

It never touches the network. This makes it handy for unit tests, automated checks, or local prototyping where you want a server and client in the same process without sockets.

`LoopbackTransport` copies data into new arrays on enqueue so the queues own their bytes. It ignores the `channelType` argument since there is no real network layer to configure reliability on.

## Channel types

The `channelType` parameter on send methods maps to Flax's `NetworkChannelType` enum. There are four values:

| Channel | Reliability | Ordering | Typical use |
| --- | --- | --- | --- |
| `ReliableOrdered` | Yes | Yes | State sync, RPCs, spawn, despawn (the default) |
| `ReliableUnordered` | Yes | No | Reliable messages where order does not matter |
| `Unreliable` | No | No | Transform updates, voice, any high-frequency latest-value data |
| `UnreliableUnordered` | No | No | Raw unreliable sends with no guarantees |

`ReliableOrdered` is the default everywhere in Reflect. Override it on a per-RPC basis through the attribute's `ChannelType` property, or pass it explicitly when calling transport methods directly.

The reason to use unreliable channels is latency on stale data. If you send position 20 times a second over a reliable ordered channel, a single dropped packet delays every subsequent update until the retransmit lands. Over an unreliable channel, a dropped packet just vanishes and the next update arrives fresh.
