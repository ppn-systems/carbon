// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Entities;

/// <summary>
/// Carbon real-time entity movement, AoI replication, and combat protocol opcodes.
/// <para>Range <c>0x1200–0x12FF</c> is reserved for the Entities module.</para>
/// </summary>
public static class EntityOpCode
{
    // ── C2S: Client → Server (0x1201–0x120F) ────────────────────────────────

    /// <summary>Client movement input command for one tick.</summary>
    public const ushort MoveInput = 0x1201;

    /// <summary>Client attack or ability activation request.</summary>
    public const ushort AttackInput = 0x1202;

    // ── S2C: Server → Client push (0x1230–0x12FF) ───────────────────────────

    /// <summary>Notifies a client that an entity entered its Area of Interest.</summary>
    public const ushort EntitySpawn = 0x1230;

    /// <summary>Notifies a client that an entity left its Area of Interest or despawned.</summary>
    public const ushort EntityDespawn = 0x1231;

    /// <summary>Batched AoI entity state deltas plus local player authoritative position.</summary>
    public const ushort EntityDeltaBatch = 0x1232;

    /// <summary>Authoritative combat hit broadcast to observers in range.</summary>
    public const ushort CombatHit = 0x1233;
}
