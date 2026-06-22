# API Reference

This is a quick map of the public types in the `Reflect` namespace and what they expose. All types are in C# and target .NET 10.

## Session

### NetworkManager

A Flax `GamePlugin` that owns the transport, server, and client. Holds a static `Instance`. `StartHost()` runs both server and client in one process for local play.

Configuration (address, port, prefabs, etc.) is pushed in by `NetworkManagerUI`. The plugin itself initializes during `Initialize()` and ticks via `Scripting.Update`.

- `static NetworkManager Instance`
- `Prefab[] SpawnablePrefabs`
- `float SyncInterval` (default 0.1)
- `string Address` (default `127.0.0.1`)
- `ushort Port` (default 7777)
- `ushort MaxConnections` (default 16)
- `event Action OnServerStarted`
- `static bool IsServer`, `static bool IsClient`, `static bool IsHost`
- `NetworkServer Server`, `NetworkClient Client`
- `void StartServer()`, `void StartClient()`, `void StartHost()` (deprecated)
- `void RebuildPrefabRegistry()`, `void RebuildTransport()`
- `void OnStart()` (reads command-line args for `server` / `client`)

### NetworkManagerUI

A Flax `Script` you place on a scene actor. It exposes the network settings in the inspector and provides editor buttons for starting and stopping the server, client, and host. After a short delay (0.25s) it pushes its settings into `NetworkManager` and calls `OnStart()`.

- `Prefab[] SpawnablePrefabs`
- `float SyncInterval` (default 0.1)
- `string Address` (default `127.0.0.1`)
- `ushort Port` (default 7777)
- `ushort MaxConnections` (default 16)
- `[ShowInEditor] bool Initialized`, `bool IsHost`, `bool IsServer`, `bool IsClient`
- `[Button] void StartHost()` (deprecated), `void StartServer()`, `void StartClient()`
- `[Button] void StopServer()`, `void StopClient()`

### NetworkServer

Created by `NetworkManager` with a transport and a prefab registry. Manages spawned objects, connections, and observer visibility on the server.

- `Dictionary<uint, NetworkIdentity> Spawned`
- `Dictionary<int, NetworkConnection> Connections`
- `IInterestManagement Interest` (default `GlobalInterest`)
- `float RebuildInterval` (default 1.0, seconds between observer rebuilds)
- `bool Active`
- `event Action<NetworkConnection> OnPlayerConnected`
- `event Action<NetworkConnection> OnPlayerLoaded`
- `event Action<NetworkConnection?> OnPlayerDisconnected`
- `void Start()`, `void Stop()`
- `NetworkIdentity Spawn(Prefab prefab, Vector3 pos, Quaternion rot, NetworkConnection owner = null)`
- `void Despawn(NetworkIdentity ni)`
- `void SendToObservers(NetworkIdentity ni, ArraySegment<byte> data, NetworkChannelType channelType)`
- `void SendToConnection(NetworkConnection conn, ArraySegment<byte> data, NetworkChannelType channelType)`
- `void RebuildObservers()`
- `void Update()`

Spawning and despawning now go through the interest management system. When an object spawns, the server calls `RebuildObservers()`, which asks the current `IInterestManagement` implementation which objects each connection should see, then sends spawn messages only to connections that newly need to see an object and despawn (hide) messages to connections that should stop seeing it. `Update()` periodically calls `RebuildObservers()` (every `RebuildInterval` seconds) to keep visibility current as objects move.

### NetworkClient

Created by `NetworkManager`. Manages spawned objects and incoming messages on the client.

- `Dictionary<uint, NetworkIdentity> Spawned` (in host mode, returns the server's `Spawned` dict)
- `Dictionary<ulong, NetworkIdentity> SceneObjects`
- `bool Active`
- `event Action OnReady`
- `void Connect()`, `void Disconnect()`
- `void Send(ArraySegment<byte> data, NetworkChannelType channelType)`

In host mode, spawn, despawn, and state messages are skipped because the server already created and owns the objects. RPCs still flow through normally.

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

## Interest Management

### IInterestManagement

Interface for controlling which networked objects each connection can see. The server calls `Rebuild` with all spawned identities, then calls `GatherVisible` per connection to determine the visible set.

- `void Rebuild(IReadOnlyCollection<NetworkIdentity> all)`
- `void GatherVisible(NetworkConnection conn, HashSet<uint> result)`

### GlobalInterest

Default implementation. Every connection sees every object. No spatial filtering.

### DistanceInterest

A connection sees objects within a fixed range (5000 units) of its owned player. Objects outside the range are hidden.

### GridInterest

Spatial hash grid with 1000-unit cells and a 2-cell view radius. Uses hysteresis (1 extra cell) so objects at the edge of visibility do not flicker in and out. Objects already being observed stay visible in the hysteresis ring even if they step just outside the tight view radius, until they move far enough to leave the hysteresis zone entirely.

This is what `NetworkManager.StartServer()` assigns by default.

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

- `T this[int index]` (get + set, set records an op, `[NoSerialize]`)
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

- `TValue this[TKey key]` (get + set, set records an op, `[NoSerialize]`)
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
