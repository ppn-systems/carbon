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

    /// <summary>Client request to pick up a nearby ground loot entity.</summary>
    public const ushort PickupLoot = 0x1203;

    /// <summary>Client request to spend an unspent attribute or skill mastery point.</summary>
    public const ushort AllocateCharacterPoint = 0x1204;

    // ── S2C: Server → Client push (0x1230–0x12FF) ───────────────────────────

    /// <summary>Notifies a client that an entity entered its Area of Interest.</summary>
    public const ushort EntitySpawn = 0x1230;

    /// <summary>Notifies a client that an entity left its Area of Interest or despawned.</summary>
    public const ushort EntityDespawn = 0x1231;

    /// <summary>Batched AoI entity state deltas plus local player authoritative position.</summary>
    public const ushort EntityDeltaBatch = 0x1232;

    /// <summary>Authoritative combat hit broadcast to observers in range.</summary>
    public const ushort CombatHit = 0x1233;

    /// <summary>Broadcasts visible armor, active weapon, enhancement sheen, and level changes to observers.</summary>
    public const ushort EntityEquipmentChanged = 0x1234;

    /// <summary>Unilateral push synchronizing local player level, XP, HP, MP, stamina, defense, and skill cooldowns.</summary>
    public const ushort PlayerStatsSync = 0x1235;

    /// <summary>Broadcasts active skill cast visuals to nearby observers.</summary>
    public const ushort SkillCastBroadcast = 0x1236;

    /// <summary>Unilateral push of the local player's server-authoritative gold and diamond balances (ADR-012).</summary>
    public const ushort WalletSync = 0x1237;

    /// <summary>Broadcasts the start of a server-authoritative monster attack phase (telegraph, release, impact) to observers.</summary>
    public const ushort EntityAttackPhase = 0x1238;
}
