# API reference

The public types in the `Reflect` namespace and what they expose.

## Session

### NetworkManager

A Flax `GamePlugin` that owns the transport, server, and client. Holds a static `Instance`. A process runs either the server or the client: starting one while the other is active logs a warning and does nothing.

Configuration (address, port, prefabs, etc.) comes from the `ReflectSettings` asset, re-read on every start. The plugin itself initializes during `Initialize()` and ticks via `Scripting.Update`.

- `static NetworkManager Instance`
- `ReflectSettings Settings`
- `event Action OnServerStarted`
- `event Action<DisconnectInfo> OnClientDisconnected`
- `static bool IsServer`, `static bool IsClient`
- `NetworkServer Server`, `NetworkClient Client`
- `IServerAuthenticator ServerAuthenticator`, `IClientAuthenticator ClientAuthenticator` (handed to the next server or client started)
- `void StartServer()`, `void StartClient()`
- `void ReloadSettings()` (no-op while a server or client is active)

When the first scene loads in play mode, the manager starts the role named on the command line (`server` / `client`), once per process.

### ReflectSettings

Project-wide config, stored as a Json asset and registered under Game Settings > Custom Settings with the key `Reflect` (`ReflectSettings.Key`).

- `Prefab[] SpawnablePrefabs`
- `float SyncInterval` (default 0.1)
- `string Address` (default `127.0.0.1`)
- `ushort Port` (default 7777)
- `ushort MaxConnections` (default 16)
- `static ReflectSettings Load()` (defaults plus a warning when no asset is registered)

### NetworkServer

Created by `NetworkManager` with a transport and a prefab registry. Manages spawned objects, connections, and observer visibility on the server.

- `Dictionary<uint, NetworkIdentity> Spawned`
- `Dictionary<int, NetworkConnection> Connections`
- `IInterestManagement Interest` (default `GlobalInterest`)
- `IServerAuthenticator Authenticator` (default null, admits everyone)
- `float AuthTimeout` (default 15, seconds before an unauthenticated connection is kicked)
- `void Admit(NetworkConnection conn)` (lets an authenticating connection join)
- `float RebuildInterval` (default 1.0, seconds between observer rebuilds)
- `bool Active`
- `SceneReference ActiveScene`, `bool IsLoadingScene`
- `void ChangeScene(SceneReference scene)`
- `event Action<SceneReference> OnSceneLoaded`
- `event Action<NetworkConnection> OnPlayerConnected`
- `event Action<NetworkConnection> OnPlayerLoaded` (once per connection per scene)
- `event Action<NetworkConnection?> OnPlayerDisconnected`
- `void Start()`, `void Stop()`
- `void Kick(NetworkConnection conn, string message = "")` (also works on connections still authenticating)
- `NetworkIdentity Spawn(Prefab prefab, Vector3 pos, Quaternion rot, NetworkConnection owner = null)` (fails while loading a scene)
- `void Despawn(NetworkIdentity ni)`
- `void SendToObservers(NetworkIdentity ni, ArraySegment<byte> data, NetworkChannelType channelType)`
- `void SendToConnection(NetworkConnection conn, ArraySegment<byte> data, NetworkChannelType channelType)`
- `void RebuildObservers()`
- `void Update()`

Spawning and despawning go through the interest management system. When an object spawns, the server calls `RebuildObservers()`, which asks the current `IInterestManagement` implementation which objects each connection should see, adds every object the connection owns, then sends spawn messages to connections that should start seeing an object and despawn messages to connections that should stop. `Update()` periodically calls `RebuildObservers()` (every `RebuildInterval` seconds) to keep visibility current as objects move.

### NetworkClient

Created by `NetworkManager`. Manages spawned objects and incoming messages on the client.

- `Dictionary<uint, NetworkIdentity> Spawned`
- `Dictionary<Guid, NetworkIdentity> SceneObjects`
- `bool Active`
- `IClientAuthenticator Authenticator` (default null)
- `SceneReference ActiveScene`, `bool IsLoadingScene`
- `event Action<SceneReference> OnSceneChanging`
- `event Action OnReady` (once per scene)
- `event Action<DisconnectInfo> OnDisconnected` (once per session, after teardown)
- `void Connect()`, `void Disconnect()`
- `void Send(ArraySegment<byte> data, NetworkChannelType channelType)`

### Authentication

Optional login handshake. With a server `Authenticator` set, a new connection waits outside `Connections` in `ConnectionPhase.Authenticating`. The client's `IClientAuthenticator.WritePayload` runs once the transport connects, and the server hands that payload to `IServerAuthenticator.OnPayload`. The authenticator then calls `Admit` or `Kick`, now or after an outside check. Until then the connection may only send its payload, and it gets no `OnPlayerConnected`. Bots skip authentication.

- `IServerAuthenticator`: `void OnPayload(NetworkServer server, NetworkConnection conn, NetworkReader payload)`, `void OnDisconnected(NetworkConnection conn)` (admitted or not)
- `IClientAuthenticator`: `void WritePayload(NetworkWriter writer)`, `void OnDisconnected()`

### DisconnectInfo

`readonly record struct DisconnectInfo(DisconnectReason Reason, string Message = "")`. `Message` is the server's text for a kick, otherwise empty.

`DisconnectReason`:

- `ClientRequested`: this client called `Disconnect()`
- `ConnectFailed`: the connection closed before it was established, usually an unreachable server
- `ServerClosed`: the server closed the connection without a reason, for example by shutting down
- `ConnectionLost`: the connection timed out
- `Kicked`: the server called `Kick`
- `SceneLoadFailed`: the server asked for a scene this client could not load

### NetworkIdentity

A Flax `Script` placed on the root actor of any networked object. Holds the network ID and references to its `NetworkScript` components.

- `uint NetId` (internal set)
- `Guid SceneId` (internal set)
- `bool IsSceneObject`
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

## Interest management

### IInterestManagement

Interface for controlling which networked objects each connection can see. The server calls `Rebuild` with all spawned identities, then calls `GatherVisible` per connection to determine the visible set. The server adds objects a connection owns on top, so implementations do not need to.

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
- `void WriteEnum(TypeCode code, object value)`
- `void Write<T>(T value)`
- `void BeginMessage(MsgType type)`, `void FinishMessage()`

`WriteInt` and `WriteLong` use ZigZag plus varint encoding. `Write<T>` supports enums automatically. `WriteEnum` is the low-level form and takes the enum's underlying integral `TypeCode`.

### NetworkReader

Reads primitives back from a byte buffer. Point it at a segment with `SetBuffer`.

- `int Position`, `int EndPosition`, `bool HasMore`
- `void SetBuffer(ArraySegment<byte> seg)`
- `byte ReadByte()`, `int ReadInt()`, `long ReadLong()`
- `float ReadFloat()`, `uint ReadUIntVar()`, `ulong ReadULongVar()`
- `string ReadString()`, `Guid ReadGuid()`, `ArraySegment<byte> ReadBytes()`
- `object ReadEnum(TypeCode code)`
- `T Read<T>()`
- `int ReadLength()`, `void SkipRemaining(int msgEnd)`

`Read<T>` returns the requested enum type. `ReadEnum` returns its underlying integral value as an `object`; it does not construct the enum itself.

### Serializer&lt;T&gt;

Generic static access point for per-type serializers. Enums, one-dimensional arrays and `List<T>` of supported types register themselves on first use.

- `static WriteFunc<T> Write`
- `static ReadFunc<T> Read`

### Serializers

Static registry and initializer for the built-in type serializers.

- `static void Init()` registers the built-in types. `NetworkManager.Initialize()` calls it automatically.
- `static void RegEnum<T>()` registers an enum explicitly and throws `InvalidOperationException` for a non-enum type. Normal enum use does not need this call.
- `static void WriteBoxed(NetworkWriter w, Type t, object v)`
- `static object ReadBoxed(NetworkReader r, Type t)`

Built-in types: `bool`, `byte`, `int`, `uint`, `long`, `ulong`, `float`, `string`, `Guid`, `Float3`, `Quaternion`, `NetworkRef`.

Enums work through both the generic and boxed APIs without manual registration, including `[Flags]` combinations and all eight C# enum backing types. Reflect sends numeric values, not names or type metadata. Both peers must agree on the enum's backing type and numeric assignments.

| Enum backing type | Encoding |
| --- | --- |
| `byte`, `sbyte` | One byte |
| `short`, `int` | ZigZag plus a 32-bit varint |
| `ushort`, `uint` | Unsigned 32-bit varint |
| `long` | ZigZag plus a 64-bit varint |
| `ulong` | Unsigned 64-bit varint |

Enum support does not register `sbyte`, `short`, or `ushort` as standalone serializer types. Deserialization preserves undefined enum values and does not call `Enum.IsDefined`; validate values in gameplay code when only specific choices are allowed.

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
- `LoadScene = 8`, `SceneReady = 9`, `Disconnect = 10`

### Msg

Helpers for the message header byte.

- `static void WriteHeader(NetworkWriter w, MsgType t)`
- `static MsgType ReadHeader(NetworkReader r)`

## Editor

### ReflectEditorPlugin

An editor-only Flax `EditorPlugin` that adds a `Start Server` / `Stop Server` toolbar button during Play mode. It launches a separate headless editor process using the same project and first loaded scene, forwards its logs to the editor, and auto-starts the local client after the server is ready. Stopping the child process or ending Play mode disconnects the auto-started client. An unexpected process exit uses the same cleanup.

The button never calls `NetworkManager.StartServer()` in the editor process. See [Getting started](/getting-started#in-the-editor) for setup and saved-scene requirements.

## Transport

### ITransport

The transport contract. See the [Transport layer](/transport) page for the full definition.

### FlaxTransport

ENet/UDP transport wrapping Flax's `NetworkPeer`. Properties: `Address`, `Port`, `MaxConnections`, `MessageSize`, `MessagePoolSize`.

### LoopbackTransport

In-process transport for testing. No network. Single simulated connection (ID `1`).

### NetworkConnection

Represents a connected client on the server side.

- `int Id`
- `ConnectionPhase Phase` (`Authenticating`, `LoadingScene`, `Syncing`, `InGame`)
- `object AuthData` (set by the server's authenticator, such as a verified account)
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
