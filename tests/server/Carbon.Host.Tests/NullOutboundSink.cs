// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Abstractions.World;

namespace Carbon.Host.Tests;

/// <summary>An outbound sink that drops every message without allocating, for tick cost measurements.</summary>
internal sealed class NullOutboundSink : IWorldOutboundSink
{
    public void SendEntitySpawn(ulong connectionId, in EntitySpawnSnapshot spawn)
    {
    }

    public void SendEntityDespawn(ulong connectionId, ulong networkId, ulong serverTick)
    {
    }

    public void SendEntityDeltaBatch(ulong connectionId, ulong serverTick, uint lastProcessedClientTick, float selfX, float selfY, ReadOnlySpan<EntityDeltaEntry> entries)
    {
    }

    public void SendPropChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, uint revision, float durability, bool isDestroyed)
    {
    }

    public void SendStructureChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, byte structureType, float durability, byte doorState, byte connectivity)
    {
    }

    public void SendConstructionChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, byte structureType, byte rotation, byte stage, byte size, float progress, bool isDeconstruction)
    {
    }

    public void SendCombatHit(ReadOnlySpan<ulong> recipientConnectionIds, ulong attackerNetworkId, ulong defenderNetworkId, float damage, float remainingHealth, bool isCritical, bool isKilled)
    {
    }

    public void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability, byte upgradeLevel = 0, float freshnessSeconds = 0f)
    {
    }

    public void SendEntityEquipmentChanged(ReadOnlySpan<ulong> recipientConnectionIds, ulong entityNetworkId, ushort headId, ushort chestId, ushort legsId, ushort feetId, ushort weaponId, byte weaponUpgrade, ushort level, float maxHealth = 0f)
    {
    }

    public void SendPlayerStatsSync(ulong connectionId, in PlayerStatsSnapshot snapshot)
    {
    }

    public void SendWalletSync(ulong connectionId, long gold, long diamonds, ulong sequence)
    {
    }

    public void SendSkillCastBroadcast(ReadOnlySpan<ulong> recipientConnectionIds, ulong casterNetworkId, byte abilitySlot, float originX, float originY, float aimX, float aimY)
    {
    }

    public void SendEntityAttackPhase(ReadOnlySpan<ulong> recipientConnectionIds, in AttackPhaseSnapshot snapshot)
    {
    }
}
