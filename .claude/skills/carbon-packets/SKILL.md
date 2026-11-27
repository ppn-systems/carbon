---
name: carbon-packets
description: Use when defining, modifying, or reviewing packet classes in contracts/ — [Packet], [GenerateFormatter], [SerializePackable], [SerializeOrder], Opcode allocation, Delta compression, and pooling.
---

# Carbon Packet Protocol & Serialization

Applies to all network packet contracts defined in `contracts/`.

## Non-negotiables

- **`[SerializePackable]` is mandatory** on every wire contract.
- **Append-only `[SerializeOrder]`**:
  - `[SerializeOrder(n)]` numbers determine positional serialization layout.
  - **NEVER renumber or remove an existing field.** New fields must always be appended with a strictly greater order number.
  - Modifying or reordering slots causes silent wire corruption between differing client/server builds.
- **Opcode Uniqueness**:
  - Every packet carries a distinct `ushort` Opcode via `public static ushort StaticOpCode => (ushort)GameOpCode.X;`.
  - Duplicate opcodes must fail the build.
- **Zero-Allocation Pooling**:
  - Server sends on the tick path lease packets from `PacketPool.Rent<T>()` (`server/Carbon.Host/PacketPool.cs`, ADR-043) inside a `using` scope around the awaited send, so the packet returns only after the send completes and is reset (`ResetForPool`) for the next recipient. Never `new` a packet on the hot path.
  - `PacketFactory<T>.Acquire()` allocates a new packet on every call in our setup (ADR-043). Use it only on cold paths, such as handler replies.
  - A field with an initializer (`Level = 1`, `EatProgress = -1`) resets to the type default, not the initializer: the sender must set it on every send (`PacketResetCoverageTests` checks this).

## Packet Definition Blueprint

```csharp
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Packets;

[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class PlayerMoveRequest : PacketBase<PlayerMoveRequest>, IPacketStaticOpcode
{
    public static ushort StaticOpCode => (ushort)CarbonOpCode.PlayerMoveRequest;

    [SerializeOrder(0)] public ulong EntityId { get; set; }
    [SerializeOrder(1)] public float PositionX { get; set; }
    [SerializeOrder(2)] public float PositionY { get; set; }
    [SerializeOrder(3)] public float PositionZ { get; set; }
    [SerializeOrder(4)] public float VelocityX { get; set; }
    [SerializeOrder(5)] public float VelocityY { get; set; }
    [SerializeOrder(6)] public float VelocityZ { get; set; }
    [SerializeOrder(7)] public uint ClientTick { get; set; }

    [SerializeIgnore]
    public long ReceivedTimestamp { get; set; } // Local telemetry only, not transmitted over the wire
}
```

## Opcode Partitioning

Opcodes (`ushort`) are structured into functional bands to avoid conflicts:

| Range | Domain | Examples |
| :--- | :--- | :--- |
| `0x0001 - 0x00FF` | System & Handshake | `Ping`, `Pong`, `HandshakeRequest`, `HandshakeResponse`, `SessionResume` |
| `0x0100 - 0x02FF` | Authentication & Account | `LoginRequest`, `LoginResult`, `CharacterListRequest`, `CharacterSelect` |
| `0x0300 - 0x0FFF` | Player & Input (C2S) | `PlayerMoveRequest`, `PlayerActionRequest`, `CastSkillRequest`, `InteractEntity` |
| `0x1000 - 0x2FFF` | World State & Snapshots (S2C) | `WorldSnapshotSync`, `EntitySpawnNotification`, `EntityDespawnNotification`, `EntityDeltaSync` |
| `0x3000 - 0x3FFF` | Combat & Status Events | `DamageEvent`, `HealEvent`, `BuffApplied`, `EntityDeath` |
| `0x4000 - 0x4FFF` | Inventory & Items | `InventorySync`, `ItemEquipRequest`, `ItemDropNotification` |
| `0x5000 - 0x5FFF` | Social & Chat | `ChatMessageRequest`, `ChatMessageBroadcast`, `PartyInvite` |

## Delta Compression

For high-frequency entity updates (positions, health, mana):
- Send full snapshot upon entity entering player's Area of Interest (AoI).
- Subsequent updates transmit `EntityDeltaSync` containing a dirty bitmask flag and only changed properties, reducing bandwidth consumption by up to 90%.
