# SyncVars

Reflect gives you three synchronized field types: `SyncVar<T>` for single values, `SyncList<T>` for ordered collections, and `SyncDictionary<TKey, TValue>` for keyed lookups. All three implement `ISyncVar`, which means the server tracks their changes and ships deltas to clients automatically.

## Declaring a SyncVar

Declare SyncVars as readonly fields on your `NetworkScript` subclass. Initialize them with a starting value:

```csharp
using Reflect;

public class PlayerHealth : NetworkScript
{
    public readonly SyncVar<int> Health = new(100);
    public readonly SyncVar<string> DisplayName = new("Player");
}
```

Read and write through the `Value` property. An implicit conversion to `T` means you can often skip `.Value` on reads:

```csharp
public void TakeDamage(int amount)
{
    if (!IsServer) return;
    Health.Value -= amount;   // writes through the property, marks dirty
}

public void ShowHealth()
{
    int current = Health;     // implicit conversion, same as Health.Value
}
```

The `SyncVar<T>` constructor takes an initial value and an optional change hook:

```csharp
public readonly SyncVar<int> Health = new(100, OnHealthChanged);

private static void OnHealthChanged(int old, int current)
{
    Debug.Log($"Health went from {old} to {current}");
}
```

## Dirty tracking

Writing to `Value` only marks the SyncVar dirty when the new value differs from the old one. The comparison uses `EqualityComparer<T>.Default`, so value types compare by value and reference types (like `string`) compare by their default equality.

The server calls `Server.Update()` every `SyncInterval` seconds (default 0.1). That tick walks every spawned object, finds scripts where `AnyDirty()` is true, and serializes just the changed SyncVars.

Once a SyncVar is serialized into a delta, its dirty flag clears. If you write the same value again next tick, it serializes again. There is no aggregation window beyond the sync interval.

## Delta vs full serialization

Reflect sends SyncVars in two situations, each with its own format.

On spawn, the server sends every SyncVar on every script. This is full serialization. It writes a 64-bit mask with all bits set (for the script's SyncVar count), then every value in order.

On each sync tick, the server sends only what changed. This is delta serialization. It writes a 64-bit mask where only the dirty SyncVars have their bit set, then only those values, in index order.

`NetworkScript.SerializeDelta` builds the mask, writes dirty values, and clears their flags. `SerializeFull` writes the mask and all values without touching the flags. On the receiving side, `Deserialize(NetworkReader, bool initialState)` reads the mask and applies only the set bits. When `initialState` is true (spawn), it calls `DeserializeFull`. When false (state tick), it calls `DeserializeDelta`.

The mask is always present, even in a delta with no changes. A client reading a state message always knows which SyncVars to expect by reading the mask first.

## Change hooks

The optional second constructor argument is an `Action<T, T>` that receives the old value and the new value. The hook fires on the receiving side when `Deserialize` applies a value that differs from what was there before.

Hooks fire on clients during normal state sync. They do not fire on the server when you assign locally through `Value`, because that path only sets the dirty flag. If you need to react to a change on the server too, run that logic in your setter or in your update loop.

## The 64 SyncVar limit

The dirty mask is a `ulong`, which is 64 bits. Each SyncVar on a script occupies one bit, indexed by its position in the sorted field list. If a `NetworkScript` declares more than 64 SyncVars, Reflect logs an error at runtime (`has >64 SyncVars; mask is 64-bit`) and the overflow is not serialized.

If you need more than 64 synchronized values on one object, split them across multiple `NetworkScript` components on the same actor. Each component gets its own mask and its own slot in the `NetworkIdentity.Scripts` array.

## Field ordering

Reflect discovers SyncVars by reflecting over the fields of your script type. It orders them by name using `StringComparer.Ordinal` and assigns each one its bit position in that order. Renaming a field shifts its position, which changes which bit it maps to. Keep your SyncVar field names stable across versions for the same reason you keep RPC names stable.

## SyncList

`SyncList<T>` is a synchronized list. It behaves like a regular `List<T>` but records every mutation as an operation (add, insert, set, remove, clear). The server ships those operations to clients, who apply them in order.

Declare it as a readonly field, same as SyncVar:

```csharp
public class MatchManager : NetworkScript
{
    public readonly SyncList<string> Scoreboard = new();
}
```

Mutate the list on the server through the standard methods (`Add`, `Insert`, `RemoveAt`, `Remove`, `Clear`, the indexer setter). Each mutation appends to an internal operation log. On the next sync tick, the log is serialized and sent to observing clients, then cleared.

```csharp
if (IsServer)
{
    Scoreboard.Add("Alice: 3");
    Scoreboard.Add("Bob: 1");
    Scoreboard[0] = "Alice: 4";   // updates the first entry
}
```

Clients never write to the list directly. They receive delta operations through `DeserializeDelta`, which applies them and fires events:

```csharp
Scoreboard.OnAdd += (index, value) => Debug.Log($"Entry {index}: {value}");
Scoreboard.OnSet += (index, old, current) => Debug.Log($"Entry {index} changed: {old} -> {current}");
Scoreboard.OnRemove += (index, value) => Debug.Log($"Removed {value} at {index}");
Scoreboard.OnClear += () => Debug.Log("Scoreboard cleared");
```

On spawn (full serialization), the server writes the entire list. On each tick (delta serialization), it writes only the operations since the last tick. The element type `T` must have a registered serializer.

`SyncList<T>` implements `IReadOnlyList<T>`, so you can iterate and index it on any side. Only the server should mutate it.

## SyncDictionary

`SyncDictionary<TKey, TValue>` works the same way but for keyed collections. Mutations are tracked as set, remove, and clear operations.

```csharp
public class LobbyManager : NetworkScript
{
    public readonly SyncDictionary<uint, string> PlayerNames = new();
}
```

Write through the indexer or `Add` on the server. Both record a set operation:

```csharp
if (IsServer)
{
    PlayerNames[conn.Id] = "Alice";
    PlayerNames.Remove(conn.Id);
}
```

Events fire on the client during deserialization:

```csharp
PlayerNames.OnSet += (key, value) => Debug.Log($"{key} -> {value}");
PlayerNames.OnRemove += (key, value) => Debug.Log($"Removed {value} ({key})");
PlayerNames.OnClear += () => Debug.Log("Cleared");
```

Full serialization on spawn writes every key-value pair. Delta serialization writes only the set and remove operations since the last tick. Both `TKey` and `TValue` must have registered serializers.

`SyncDictionary<TKey, TValue>` implements `IReadOnlyDictionary<TKey, TValue>`.

## The ISyncVar contract

All three types implement `ISyncVar`, which is the interface Reflect uses to discover, serialize, and deserialize synchronized fields:

```csharp
public interface ISyncVar
{
    bool IsDirty { get; }
    void ClearDirty();

    void SerializeFull(NetworkWriter w);
    void SerializeDelta(NetworkWriter w);

    void DeserializeFull(NetworkReader r);
    void DeserializeDelta(NetworkReader r);
}
```

`SerializeFull` / `DeserializeFull` handle the initial spawn (write everything, read everything). `SerializeDelta` / `DeserializeDelta` handle incremental updates (write only what changed, read and apply the changes). `SyncVar<T>` uses the same wire format for both because a single value has no structure to optimize. `SyncList<T>` and `SyncDictionary<TKey, TValue>` use the full path for initial state and the operation log for deltas.

You can implement `ISyncVar` on your own type if you need a custom synchronized structure. Declare it as a field on your `NetworkScript` and Reflect will pick it up automatically.
