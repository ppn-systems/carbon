// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;

namespace Carbon.Abstractions.World;

/// <summary>
/// Immutable per-player vitals, traits, binary progression payload, and active cooldown snapshot
/// dispatched via <see cref="IWorldOutboundSink"/>.
/// </summary>
public readonly record struct PlayerStatsSnapshot(
    ushort Level,
    uint Experience,
    uint XpToNextLevel,
    float CurrentHealth,
    float MaxHealth,
    float CurrentMana,
    float MaxMana,
    float CurrentStamina,
    float MaxStamina,
    float Defense,
    bool LeveledUp,
    CharacterTraits Traits,
    CharacterSkillMask UnlockedSkillMask,
    ushort UnspentStatPoints,
    ushort UnspentSkillPoints,
    byte[] ProgressionBlob,
    ushort[] CooldownSkillIds,
    float[] CooldownRemainingSeconds,
    PlayerNeedsSnapshot Needs = default);

/// <summary>
/// Survival needs of the local player carried inside <see cref="PlayerStatsSnapshot"/>.
/// </summary>
/// <param name="Food">Current food level.</param>
/// <param name="MaxFood">Food capacity.</param>
/// <param name="HungerState">Hunger state byte (0 Fed, 1 Hungry, 2 UrgentlyHungry, 3 Starving).</param>
/// <param name="Malnutrition">Malnutrition severity, 0..1.</param>
/// <param name="Mood">Mood, 0..100.</param>
/// <param name="FoodPoisoned">Whether the player has food poisoning.</param>
/// <param name="EatProgress">Meal progress 0..1, or negative when not eating.</param>
/// <param name="Rest">Current rest level (ADR-018).</param>
/// <param name="MaxRest">Rest capacity.</param>
/// <param name="RestState">Rest state byte (0 Rested, 1 Tired, 2 VeryTired, 3 Exhausted).</param>
/// <param name="RestMode">How the player is resting.</param>
public readonly record struct PlayerNeedsSnapshot(
    float Food,
    float MaxFood,
    byte HungerState,
    float Malnutrition,
    float Mood,
    bool FoodPoisoned,
    float EatProgress,
    float Rest = 0f,
    float MaxRest = 0f,
    byte RestState = 0,
    RestMode RestMode = RestMode.None);

/// <summary>
/// Snapshot sent by the authoritative simulation when an entity enters a player's Area of Interest.
/// </summary>
/// <remarks>
/// For ground loot, <c>LootCurrency</c> says whether it is an item stack or a gold/diamond pile, and <c>LootOwnerNetworkId</c> / <c>LootProtectionMs</c> name the only player who may pick it up and for how much longer
/// (both 0 when anyone may).
/// </remarks>
public readonly record struct EntitySpawnSnapshot(
    ulong NetworkId,
    EntityKind Kind,
    ushort ArchetypeId,
    string Name,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    float Radius,
    float CurrentHealth,
    float MaxHealth,
    ushort EquippedHeadId = 0,
    ushort EquippedChestId = 0,
    ushort EquippedLegsId = 0,
    ushort EquippedFeetId = 0,
    ushort EquippedWeaponId = 0,
    ushort Level = 1,
    int LootCount = 0,
    byte WeaponUpgradeLevel = 0,
    LootCurrency LootCurrency = LootCurrency.None,
    ulong LootOwnerNetworkId = 0UL,
    uint LootProtectionMs = 0U);

/// <summary>
/// Per-entity delta slice collected inside a tick's AoI snapshot batch.
/// </summary>
/// <param name="NetworkId">Entity network ID.</param>
/// <param name="DirtyMask">Dirty bitmask flags.</param>
/// <param name="PositionX">World X coordinate.</param>
/// <param name="PositionY">World Y coordinate.</param>
/// <param name="VelocityX">Linear X velocity.</param>
/// <param name="VelocityY">Linear Y velocity.</param>
/// <param name="CurrentHealth">Current health points.</param>
public readonly record struct EntityDeltaEntry(
    ulong NetworkId,
    byte DirtyMask,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    float CurrentHealth);

/// <summary>
/// Start of a server-authoritative attack phase dispatched as <c>EntityAttackPhase</c> (see ADR-013).
/// </summary>
/// <param name="NetworkId">Attacker network id.</param>
/// <param name="ServerTick">Tick the phase started.</param>
/// <param name="AttackKind">Attack kind byte (melee, projectile, spell, leap).</param>
/// <param name="Phase">Phase byte (none, windup, travel, active, recovery).</param>
/// <param name="Shape">Telegraph shape byte (circle, cone).</param>
/// <param name="Interrupted">Whether a none phase reports an interruption.</param>
/// <param name="TargetX">Locked target point X.</param>
/// <param name="TargetY">Locked target point Y.</param>
/// <param name="DirectionX">Facing X.</param>
/// <param name="DirectionY">Facing Y.</param>
/// <param name="AreaRadius">Telegraph radius.</param>
/// <param name="HalfAngleRadians">Cone half-angle.</param>
/// <param name="PhaseDurationTicks">Phase duration in ticks.</param>
/// <param name="Progress">Normalized whole-attack progress at phase start.</param>
public readonly record struct AttackPhaseSnapshot(
    ulong NetworkId,
    ulong ServerTick,
    byte AttackKind,
    byte Phase,
    byte Shape,
    bool Interrupted,
    float TargetX,
    float TargetY,
    float DirectionX,
    float DirectionY,
    float AreaRadius,
    float HalfAngleRadians,
    ushort PhaseDurationTicks,
    float Progress);

/// <summary>
/// Decoupled transport sink used by <c>Carbon.Application</c> to dispatch AoI entity updates and map edits
/// to active connections without referencing Nalix sockets directly.
/// </summary>
public interface IWorldOutboundSink
{
    /// <summary>Sends an entity spawn message to the target connection.</summary>
    void SendEntitySpawn(ulong connectionId, in EntitySpawnSnapshot spawn);

    /// <summary>Sends an entity despawn notification to the target connection.</summary>
    void SendEntityDespawn(ulong connectionId, ulong networkId, ulong serverTick);

    /// <summary>Sends a batched AoI entity delta update to the target connection.</summary>
    void SendEntityDeltaBatch(
        ulong connectionId,
        ulong serverTick,
        uint lastProcessedClientTick,
        float selfX,
        float selfY,
        ReadOnlySpan<EntityDeltaEntry> entries);

    /// <summary>Broadcasts a natural prop state update to connections in range.</summary>
    void SendPropChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, uint revision, float durability, bool isDestroyed);

    /// <summary>Broadcasts a constructed structure update to connections in range.</summary>
    void SendStructureChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, byte structureType, float durability, byte doorState, byte connectivity);

    /// <summary>Broadcasts a construction site or deconstruction stage / progress update to connections in range.</summary>
    void SendConstructionChanged(
        ReadOnlySpan<ulong> recipientConnectionIds,
        int tileX,
        int tileY,
        byte structureType,
        byte rotation,
        byte stage,
        byte size,
        float progress,
        bool isDeconstruction);

    /// <summary>Broadcasts a combat damage event to connections observing the combatants.</summary>
    void SendCombatHit(ReadOnlySpan<ulong> recipientConnectionIds, ulong attackerNetworkId, ulong defenderNetworkId, float damage, float remainingHealth, bool isCritical, bool isKilled);

    /// <summary>Sends a single updated inventory slot to the owning player connection.</summary>
    /// <remarks><paramref name="freshnessSeconds"/> is the food spoilage countdown (ADR-016), 0 when not perishable.</remarks>
    void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability, byte upgradeLevel = 0, float freshnessSeconds = 0f);

    /// <summary>Broadcasts updated visible armor, held weapon, enhancement glow, and level to AoI observers.</summary>
    void SendEntityEquipmentChanged(
        ReadOnlySpan<ulong> recipientConnectionIds,
        ulong entityNetworkId,
        ushort headId,
        ushort chestId,
        ushort legsId,
        ushort feetId,
        ushort weaponId,
        byte weaponUpgrade,
        ushort level,
        float maxHealth = 0f);

    /// <summary>Synchronizes the owning player's RPG vitals, flags, progression blob, and cooldown arrays.</summary>
    void SendPlayerStatsSync(ulong connectionId, in PlayerStatsSnapshot snapshot);

    /// <summary>Pushes the owning player's server-authoritative gold and diamond balances (ADR-012).</summary>
    void SendWalletSync(ulong connectionId, long gold, long diamonds, ulong sequence);

    /// <summary>Broadcasts active ability cast VFX coordinates to AoI observers.</summary>
    void SendSkillCastBroadcast(
        ReadOnlySpan<ulong> recipientConnectionIds,
        ulong casterNetworkId,
        byte abilitySlot,
        float originX,
        float originY,
        float aimX,
        float aimY);

    /// <summary>Broadcasts the start of a monster attack phase to AoI observers.</summary>
    void SendEntityAttackPhase(ReadOnlySpan<ulong> recipientConnectionIds, in AttackPhaseSnapshot snapshot);
}
