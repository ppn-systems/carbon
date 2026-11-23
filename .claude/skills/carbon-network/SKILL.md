---
name: carbon-network
description: Use when working with networking, transports, connection lifecycle, rate limiting, and tick timing in Carbon — TCP, UDP/KCP/MsQuic, WebSockets, ConnectionGuard, Snowflake session IDs, and zero-RTT reconnection.
---

# Carbon Networking Architecture

Applies to server listeners, connection multiplexing, and client transport runners.

## Multi-Transport Strategy

Carbon provides unified packet abstractions over diverse network transports:

| Target Client | Transport | Protocol | Use Case |
| :--- | :--- | :--- | :--- |
| **Desktop Client** | UDP (LiteNetLib / KCP / MsQuic) | Custom Datagram | Unreliable sequenced movement, entity snapshots, high-speed combat |
| **Desktop Client** | TCP / TLS | Stream Socket | Reliable authentication, chat, inventory, transactional RPC |
| **WebAssembly Client** | WebSocket / WebTransport | Binary WS Frames | Browser-based gameplay (WebAssembly cannot open raw UDP sockets) |

## Connection Lifecycle & Session Management

- **Snowflake Session IDs**: Every authenticated session is assigned a 64-bit Snowflake ID (`ulong`) encoding timestamp, node worker ID, and sequence counter.
- **0-RTT Reconnect / Session Resume**:
  - Flaky mobile or Wi-Fi connections can resume an existing session within a time window (e.g., 30 seconds) using a cryptographically signed resume token.
  - Resume tokens must be consumed atomically to prevent Time-of-Check to Time-of-Use (TOCTOU) replay attacks.
- **Heartbeat & Keep-Alive**:
  - Heartbeat pings occur every 5 seconds.
  - Connections with no traffic or heartbeat response for 15 seconds are transitioned to `Disconnected` and their entities in the world are scheduled for de-spawning.

## ConnectionGuard (Flood & DDoS Defense)

- **Rate Limiting**:
  - Ingress packets are limited per connection using a sliding-window counter.
  - High-frequency input channels (movement/input) have dedicated burst allowances.
- **Progressive Banning**:
  - Malformed or suspicious packet bursts trigger tiered progressive bans (Tier 1: 1 min, Tier 2: 15 min, Tier 3: 24h).
  - Anti-replay protection: UDP datagrams carry monotonically increasing sequence numbers verified against a sliding bitmask window. Replayed packets are silently discarded.

## Game Tick Timing Wheel

- **High-Resolution Clock**:
  - Network listener threads operate on I/O completion ports (`SocketAsyncEventArgs` / `System.IO.Pipelines`).
  - Ingress packets are pushed into lock-free concurrent queues.
  - The World Tick simulation loop (e.g., fixed 20Hz or 30Hz) consumes queued packets at the start of each tick:
    ```
    Tick Start -> Drain Input Queue -> Update Systems -> Physics & Collision -> Broadcast Snapshot -> Tick End
    ```
