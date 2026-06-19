# API Reference

This is a quick map of the public types in the `Reflect` namespace and what they expose. All types are in C# and target .NET 10.

## Session

### NetworkManager

A Flax `Script` that owns the transport, server, and client. Add it to a scene actor. Holds a static `Instance`.

- `static NetworkManager Instance`
- `Prefab[] SpawnablePrefabs`
- `float SyncInterval` (default 0.1)
- `string Address` (default `127.0.0.1`)
- `ushort Port` (default 7777)
- `ushort MaxConnections` (default 16)
- `event Action OnServerStarted`
- `bool IsServer`, `bool IsClient`
- `NetworkServer Server`, `NetworkClient Client`
- `void StartServer()`, `void StartClient()`

### NetworkServer

Created by `NetworkManager` with a transport and a prefab registry. Manages spawned objects and connections on the server.

- `Dictionary<uint, NetworkIdentity> Spawned`
- `Dictionary<int, NetworkConnection> Connections`
- `bool Active`
- `event Action<NetworkConnection> OnPlayerConnected`
- `event Action<NetworkConnection> OnPlayerLoaded`
- `event Action<NetworkConnection?> OnPlayerDisconnected`
- `void Start()`, `void Stop()`
- `NetworkIdentity Spawn(Prefab prefab, Vector3 pos, Quaternion rot, NetworkConnection owner = null)`
- `void Despawn(NetworkIdentity ni)`
- `void SendToObservers(NetworkIdentity ni, ArraySegment<byte> data, NetworkChannelType channelType)`
- `void SendToConnection(NetworkConnection conn, ArraySegment<byte> data, NetworkChannelType channelType)`
- `void Update()`

### NetworkClient

Created by `NetworkManager`. Manages spawned objects and incoming messages on the client.

- `Dictionary<uint, NetworkIdentity> Spawned`
- `Dictionary<ulong, NetworkIdentity> SceneObjects`
- `bool Active`
- `event Action OnReady`
- `void Connect()`, `void Disconnect()`
- `void Send(ArraySegment<byte> data, NetworkChannelType channelType)`

### NetworkIdentity

A Flax `Script` placed on the root actor of any networked object. Holds the network ID and references to its `NetworkScript` components.

- `uint NetId` (internal set)
- `ulong SceneId`
- `Guid AssetId`
- `static bool IsServer`, `static bool IsClient`
- `NetworkConnection Owner` (internal set)
- `bool IsOwnedLocally` (internal set)
- `NetworkScript[] Scripts` (lazy, assigns `Identity` and `ComponentIndex`)
- `bool AnyDirty()`

### NetworkScript

Abstract base class for all gameplay networking code. Requires a `NetworkIdentity` on the same actor.

- `NetworkIdentity Identity` (internal set)
- `byte ComponentIndex` (internal set)
- `bool IsServer`, `bool IsClient`, `bool HasAuthority`
- `ISyncVar[] SyncVars` (lazy, sorted by field name, max 64)
- `bool AnyDirty()`
- `void SerializeDelta(NetworkWriter w)`, `void SerializeFull(NetworkWriter w)`
- `void Deserialize(NetworkReader r, bool initialState)`
- `protected void SendCommand(string methodName, params object[] args)`
- `protected void SendClientRpc(string methodName, params object[] args)`
- `protected void SendTargetRpc(NetworkConnection target, string methodName, params object[] args)`
- `virtual void OnNetworkSpawn()`, `virtual void OnNetworkDespawn()`

### RpcRegistry

Static cache that discovers RPC methods by reflection and assigns stable IDs.

- `static Dictionary<ushort, RpcInfo> Get(Type t)`
- `static RpcInfo GetByName(Type t, string name)`

`RpcInfo` fields: `ushort Id`, `MethodInfo Method`, `ParameterInfo[] Params`, `RpcKind Kind`, `bool RequiresAuthority`, `NetworkChannelType ChannelType`.

`RpcKind` enum: `Command`, `ClientRpc`, `TargetRpc`.

## Attributes

### CommandAttribute

Marks a client-to-server method. Fields: `bool RequiresAuthority` (default true), `NetworkChannelType ChannelType` (default ReliableOrdered).

### ClientRpcAttribute

Marks a server-to-all-clients method. Field: `NetworkChannelType ChannelType` (default ReliableOrdered).

### TargetRpcAttribute

Marks a server-to-one-client method. Field: `NetworkChannelType ChannelType` (default ReliableOrdered).

## Core

### NetworkWriter

Writes primitives into a growable byte buffer. Starts at 1500 bytes and doubles as needed.

- `int Position`
- `void Reset()`, `ArraySegment<byte> ToSegment()`
- `void WriteByte(byte)`, `void WriteInt(int)`, `void WriteLong(long)`
- `void WriteFloat(float)`, `void WriteUIntVar(uint)`, `void WriteULongVar(ulong)`
- `void WriteString(string)`, `void WriteGuid(Guid)`, `void WriteBytes(ArraySegment<byte>)`
- `void Write<T>(T value)`
- `void BeginMessage(MsgType type)`, `void FinishMessage()`

`WriteInt` and `WriteLong` use ZigZag plus varint encoding.

### NetworkReader

Reads primitives back from a byte buffer. Point it at a segment with `SetBuffer`.

- `int Position`, `int EndPosition`, `bool HasMore`
- `void SetBuffer(ArraySegment<byte> seg)`
- `byte ReadByte()`, `int ReadInt()`, `long ReadLong()`
- `float ReadFloat()`, `uint ReadUIntVar()`, `ulong ReadULongVar()`
- `string ReadString()`, `Guid ReadGuid()`, `ArraySegment<byte> ReadBytes()`
- `T Read<T>()`
- `int ReadLength()`, `void SkipRemaining(int msgEnd)`

### Serializer&lt;T&gt;

Generic static access point for per-type serializers.

- `static WriteFunc<T> Write`
- `static ReadFunc<T> Read`

### Serializers

Static registry and initializer for the built-in type serializers.

- `static void Init()` (registers the built-in types)
- `static void WriteBoxed(NetworkWriter w, Type t, object v)`
- `static object ReadBoxed(NetworkReader r, Type t)`

Built-in types: `bool`, `byte`, `int`, `uint`, `long`, `ulong`, `float`, `string`, `Guid`, `Float3`, `Quaternion`, `NetworkRef`.

### SyncVar&lt;T&gt;

A synchronized value with dirty tracking. Implements `ISyncVar`.

- `T Value` (marks dirty on change)
- `bool IsDirty`
- `void ClearDirty()`
- `void SerializeFull(NetworkWriter w)`, `void SerializeDelta(NetworkWriter w)`
- `void DeserializeFull(NetworkReader r)`, `void DeserializeDelta(NetworkReader r)`
- `implicit operator T`

Constructor: `SyncVar<T>(T initial = default, Action<T, T> hook = null)`. The hook receives `(oldValue, newValue)` and fires during deserialization.

`ISyncVar` interface: `bool IsDirty`, `void ClearDirty()`, `void SerializeFull(NetworkWriter)`, `void SerializeDelta(NetworkWriter)`, `void DeserializeFull(NetworkReader)`, `void DeserializeDelta(NetworkReader)`.

### SyncList&lt;T&gt;

A synchronized list with operation-level delta tracking. Implements `ISyncVar` and `IReadOnlyList<T>`.

- `T this[int index]` (get + set, set records an op)
- `int Count`
- `void Add(T)`, `void Insert(int, T)`, `void RemoveAt(int)`, `bool Remove(T)`, `void Clear()`
- `int IndexOf(T)`, `bool Contains(T)`
- `event Action<int, T> OnAdd`
- `event Action<int, T, T> OnSet`
- `event Action<int, T> OnRemove`
- `event Action OnClear`

Full serialization writes the entire list. Delta serialization writes the operation log (add, insert, set, removeAt, clear) since the last tick. Events fire on the client during `DeserializeDelta`.

### SyncDictionary&lt;TKey, TValue&gt;

A synchronized dictionary with operation-level delta tracking. Implements `ISyncVar` and `IReadOnlyDictionary<TKey, TValue>`.

- `TValue this[TKey key]` (get + set, set records an op)
- `void Add(TKey, TValue)`, `bool Remove(TKey)`, `void Clear()`
- `bool ContainsKey(TKey)`, `bool ContainsValue(TValue)`, `bool TryGetValue(TKey, out TValue)`
- `int Count`
- `IEnumerable<TKey> Keys`, `IEnumerable<TValue> Values`
- `event Action<TKey, TValue> OnSet`
- `event Action<TKey, TValue> OnRemove`
- `event Action OnClear`

Full serialization writes every key-value pair. Delta serialization writes the operation log (set, remove, clear) since the last tick. Events fire on the client during `DeserializeDelta`.

### NetworkRef

A serializable reference to a networked object. On the wire it stores a `NetId`. When you resolve it, it looks up the live `NetworkIdentity` in whichever side is running (server or client).

- `uint NetId` (readonly)
- `bool IsNull`
- `NetworkIdentity Resolve()`
- `T Resolve<T>() where T : NetworkScript`
- `implicit operator NetworkRef(NetworkIdentity)`
- `implicit operator NetworkRef(NetworkScript)`

Construct from a `NetworkIdentity` or `NetworkScript`, or pass a raw `NetId`. `Resolve()` re-checks the spawned dictionary every call, so it stays correct after a despawn and respawn. Returns `null` if the target does not exist on this peer yet.

`Resolve<T>()` is a shortcut that resolves the identity and calls `GetScript<T>()` on its actor in one step.

### MsgType

Identifies a message on the wire. Byte enum.

- `Spawn = 1`, `Despawn = 2`, `State = 3`, `WelcomeDone = 4`
- `Command = 5`, `ClientRpc = 6`, `TargetRpc = 7`

### Msg

Helpers for the message header byte.

- `static void WriteHeader(NetworkWriter w, MsgType t)`
- `static MsgType ReadHeader(NetworkReader r)`

## Transport

### ITransport

The transport contract. See the [Transport Layer](/transport) page for the full definition.

### FlaxTransport

ENet/UDP transport wrapping Flax's `NetworkPeer`. Properties: `Address`, `Port`, `MaxConnections`, `MessageSize`, `MessagePoolSize`.

### LoopbackTransport

In-process transport for testing. No network. Single simulated connection (ID `1`).

### NetworkConnection

Represents a connected client on the server side.

- `int Id`
- `NetworkIdentity OwnedPlayer`
- `HashSet<uint> Observing`

## Components

### NetworkTransform

A `NetworkScript` that synchronizes position and rotation with snapshot interpolation.

- `float SendRate` (default 20)
- `float InterpolationDelay` (default 0.1)
- `float PositionThreshold` (default 0.01), `float RotationThreshold` (default 0.1)
- `bool ClientAuthority` (default true)
- `void ServerOverridePosition(Float3 pos)`
- `override void OnNetworkSpawn()`, `override void OnUpdate()`

Internally uses `[Command]` and `[ClientRpc]` on the `Unreliable` channel for movement updates.
