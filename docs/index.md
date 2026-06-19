---
layout: home

hero:
  name: Reflect
  text: Client-server networking for Flax Engine
  tagline: Mirror-inspired networking, extracted from a real multiplayer game. C# only, targets .NET 10 and Flax Engine 1.12.
  actions:
    - theme: brand
      text: Get Started
      link: /getting-started
    - theme: alt
      text: GitHub
      link: https://github.com/Bill3621/Reflect

features:
  - title: RPCs
    details: Three attributes cover the common cases. [Command] goes client to server, [ClientRpc] fans out to all clients, [TargetRpc] hits a single connection.
  - title: SyncVars
    details: SyncVar<T> tracks its own dirty state and serializes deltas with a 64-bit mask, so you only send what changed.
  - title: NetworkTransform
    details: Client-authoritative movement with snapshot interpolation, configurable send rate, and movement thresholds built in.
  - title: Swappable transport
    details: An ITransport interface sits between Reflect and the wire. FlaxTransport ships with it (ENet/UDP). LoopbackTransport is there for tests.
  - title: Compact wire format
    details: NetworkWriter and NetworkReader use varint and ZigZag encoding. Small integers take one byte, signed values stay small.
  - title: Per-message reliability
    details: Each RPC and each state update picks its own channel. State sync goes reliable ordered. Movement and voice go unreliable.
---
