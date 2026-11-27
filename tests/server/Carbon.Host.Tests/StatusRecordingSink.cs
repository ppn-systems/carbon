// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;

namespace Carbon.Host.Tests;

/// <summary>Records status effect syncs, tinted combat hits and the owner's position (ADR-029 tests).</summary>
internal sealed class StatusRecordingSink : IWorldOutboundSink
{
    public void SendEntitySpawn(ulong connectionId, in EntitySpawnSnapshot spawn)
    {
    }

    public void SendEntityDespawn(ulong connectionId, ulong networkId)
    {
    }

    public List<(ulong ConnectionId, StatusEffectEntry[] Entries)> StatusSyncs { get; } = [];

    public List<(ulong[] Recipients, ulong DefenderNetworkId, float Damage, float RemainingHealth, ushort StatusEffectId, byte StatusEffectLevel)> CombatHits { get; } = [];

    public Dictionary<ulong, Vector2> SelfPositions { get; } = [];

    public List<(ulong ConnectionId, PlayerOxygenSnapshot Oxygen)> OxygenSyncs { get; } = [];

    public List<(ulong ConnectionId, PlayerDeathSnapshot Death)> Deaths { get; } = [];

    public void SendPlayerOxygen(ulong connectionId, in PlayerOxygenSnapshot oxygen) =>
        this.OxygenSyncs.Add((connectionId, oxygen));

    public void SendPlayerDeath(ulong connectionId, in PlayerDeathSnapshot death) =>
        this.Deaths.Add((connectionId, death));

    public void SendStatusEffects(ulong connectionId, ReadOnlySpan<StatusEffectEntry> entries) =>
        this.StatusSyncs.Add((connectionId, entries.ToArray()));

    public void SendCombatHit(
        ReadOnlySpan<ulong> recipientConnectionIds,
        ulong attackerNetworkId,
        ulong defenderNetworkId,
        float damage,
        float remainingHealth,
        bool isCritical,
        bool isKilled,
        ushort statusEffectId,
        byte statusEffectLevel) =>
        this.CombatHits.Add((recipientConnectionIds.ToArray(), defenderNetworkId, damage, remainingHealth, statusEffectId, statusEffectLevel));

    public void SendEntityDeltaBatch(ulong connectionId, ulong serverTick, uint lastProcessedClientTick, float selfX, float selfY, ReadOnlySpan<EntityDeltaEntry> entries) =>
        this.SelfPositions[connectionId] = new Vector2(selfX, selfY);

    public void SendPropChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, float durability, bool isDestroyed)
    {
    }

    public void SendStructureChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, byte structureType, byte doorState)
    {
    }

    public void SendConstructionChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, byte structureType, byte rotation, byte stage, byte size, float progress, bool isDeconstruction)
    {
    }

    public void SendCombatHit(ReadOnlySpan<ulong> recipientConnectionIds, ulong attackerNetworkId, ulong defenderNetworkId, float damage, float remainingHealth, bool isCritical, bool isKilled) =>
        this.SendCombatHit(recipientConnectionIds, attackerNetworkId, defenderNetworkId, damage, remainingHealth, isCritical, isKilled, 0, 0);

    public void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability, byte upgradeLevel = 0, float freshnessSeconds = 0f)
    {
    }

    public void SendEntityEquipmentChanged(ReadOnlySpan<ulong> recipientConnectionIds, ulong entityNetworkId, ushort headId, ushort chestId, ushort legsId, ushort feetId, ushort weaponId, byte weaponUpgrade, ushort level, float maxHealth = 0f)
    {
    }

    public Dictionary<ulong, PlayerStatsSnapshot> LastStats { get; } = [];

    public void SendPlayerStatsSync(ulong connectionId, in PlayerStatsSnapshot snapshot) =>
        this.LastStats[connectionId] = snapshot;

    public void SendWalletSync(ulong connectionId, long gold, long diamonds, ulong sequence)
    {
    }

    public void SendSkillCastBroadcast(ReadOnlySpan<ulong> recipientConnectionIds, ulong casterNetworkId, byte abilitySlot, float aimX, float aimY)
    {
    }

    public void SendEntityAttackPhase(ReadOnlySpan<ulong> recipientConnectionIds, in AttackPhaseSnapshot snapshot)
    {
    }

    public void SendEnhancementAnnouncement(ReadOnlySpan<ulong> recipientConnectionIds, string playerName, ushort itemId, EnhancementOutcome outcome, byte level)
    {
    }
}
