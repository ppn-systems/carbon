// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;

namespace Carbon.Application.World.Replication;

internal readonly record struct OutboundSpawn(ulong ConnectionId, EntitySpawnSnapshot Snapshot);

internal readonly record struct OutboundDespawn(ulong ConnectionId, ulong NetworkId, ulong ServerTick);

internal readonly record struct OutboundDelta(
    ulong ConnectionId,
    ulong ServerTick,
    uint LastClientTick,
    float SelfX,
    float SelfY,
    int Offset,
    int Count);

internal readonly record struct OutboundCombatHit(
    int RecipientsOffset,
    int RecipientsCount,
    ulong AttackerNetworkId,
    ulong DefenderNetworkId,
    float Damage,
    float RemainingHealth,
    bool IsCritical,
    bool IsKilled,
    ushort StatusEffectId = 0,
    byte StatusEffectLevel = 0);

internal readonly record struct OutboundSlotChange(ulong ConnectionId, ushort SlotIndex, ItemSlot Slot);

/// <summary>An item about to break, or broken and destroyed, pushed to its owner (ADR-035).</summary>
internal readonly record struct OutboundDurabilityAlert(ulong ConnectionId, DurabilityAlertKind Kind, ushort SlotIndex, uint ItemId, byte UpgradeLevel);

internal readonly record struct OutboundPropChange(
    int RecipientsOffset,
    int RecipientsCount,
    int TileX,
    int TileY,
    uint Revision,
    float Durability,
    bool IsDestroyed);

internal readonly record struct OutboundStructureChange(
    int RecipientsOffset,
    int RecipientsCount,
    int TileX,
    int TileY,
    byte StructureType,
    float Durability,
    byte DoorState,
    byte Connectivity);

internal readonly record struct OutboundConstructionChange(
    int RecipientsOffset,
    int RecipientsCount,
    int TileX,
    int TileY,
    byte StructureType,
    byte Rotation,
    byte Stage,
    byte Size,
    float Progress,
    bool IsDeconstruction);

internal readonly record struct OutboundEquipmentChange(
    int RecipientsOffset,
    int RecipientsCount,
    ulong NetworkId,
    ushort HeadId,
    ushort ChestId,
    ushort LegsId,
    ushort FeetId,
    ushort WeaponId,
    byte WeaponUpgrade,
    ushort Level,
    float MaxHealth);

internal readonly record struct OutboundStatsSync(ulong ConnectionId, PlayerStatsSnapshot Snapshot);

/// <summary>The owner's status effect list (ADR-029 D9), a slice of <see cref="OutboundBatch.StatusEntries"/>.</summary>
internal readonly record struct OutboundStatusSync(ulong ConnectionId, int Offset, int Count);

internal readonly record struct OutboundWalletSync(ulong ConnectionId, long Gold, long Diamonds, ulong Sequence);

/// <summary>A player's death pushed to that player (ADR-031).</summary>
internal readonly record struct OutboundPlayerDeath(ulong ConnectionId, PlayerDeathSnapshot Death);

internal readonly record struct OutboundStaminaStatus(ulong ConnectionId, bool Exhausted, uint RefusalCount);

/// <summary>A player's swim state and oxygen pushed to that player (ADR-032).</summary>
internal readonly record struct OutboundOxygenSync(ulong ConnectionId, PlayerOxygenSnapshot Oxygen);

/// <summary>A trader's stock pushed to one player (ADR-025).</summary>
internal readonly record struct OutboundTraderStock(ulong ConnectionId, TraderStockSnapshot Stock);

internal readonly record struct OutboundSkillCast(
    int RecipientsOffset,
    int RecipientsCount,
    ulong CasterNetworkId,
    byte AbilitySlot,
    float OriginX,
    float OriginY,
    float AimX,
    float AimY);

internal readonly record struct OutboundAttackPhase(int RecipientsOffset, int RecipientsCount, AttackPhaseSnapshot Snapshot);

internal readonly record struct OutboundEnhancementAnnouncement(
    int RecipientsOffset,
    int RecipientsCount,
    ulong PlayerNetworkId,
    string PlayerName,
    ushort ItemId,
    EnhancementOutcome Outcome,
    byte Level);

/// <summary>
/// One tick's worth of outbound messages. Multicast messages store a slice (offset, count) into <see cref="Recipients"/>;
/// entity deltas store a slice into <see cref="DeltaEntries"/>. All lists are reused across ticks.
/// </summary>
internal sealed class OutboundBatch
{
    public List<OutboundSpawn> Spawns { get; } = new(64);

    public List<OutboundDespawn> Despawns { get; } = new(64);

    public List<OutboundDelta> Deltas { get; } = new(64);

    public List<EntityDeltaEntry> DeltaEntries { get; } = new(512);

    public List<OutboundCombatHit> CombatHits { get; } = new(32);

    public List<OutboundSlotChange> SlotChanges { get; } = new(32);

    public List<OutboundDurabilityAlert> DurabilityAlerts { get; } = new(8);

    public List<OutboundPropChange> PropChanges { get; } = new(16);

    public List<OutboundStructureChange> StructureChanges { get; } = new(16);

    public List<OutboundConstructionChange> ConstructionChanges { get; } = new(16);

    public List<OutboundEquipmentChange> EquipmentChanges { get; } = new(16);

    public List<OutboundStatsSync> StatsSyncs { get; } = new(32);

    public List<OutboundWalletSync> WalletSyncs { get; } = new(16);

    public List<OutboundStatusSync> StatusSyncs { get; } = new(16);

    public List<StatusEffectEntry> StatusEntries { get; } = new(64);

    public List<OutboundStaminaStatus> StaminaStatuses { get; } = new(16);

    public List<OutboundTraderStock> TraderStocks { get; } = new(4);

    public List<OutboundPlayerDeath> PlayerDeaths { get; } = new(4);

    public List<OutboundOxygenSync> OxygenSyncs { get; } = new(32);

    public List<OutboundSkillCast> SkillCasts { get; } = new(16);

    public List<OutboundAttackPhase> AttackPhases { get; } = new(32);

    public List<OutboundEnhancementAnnouncement> EnhancementAnnouncements { get; } = new(4);

    public List<ulong> Recipients { get; } = new(256);

    public void Clear()
    {
        this.Spawns.Clear();
        this.Despawns.Clear();
        this.Deltas.Clear();
        this.DeltaEntries.Clear();
        this.CombatHits.Clear();
        this.SlotChanges.Clear();
        this.DurabilityAlerts.Clear();
        this.PropChanges.Clear();
        this.StructureChanges.Clear();
        this.ConstructionChanges.Clear();
        this.EquipmentChanges.Clear();
        this.StatsSyncs.Clear();
        this.StaminaStatuses.Clear();
        this.WalletSyncs.Clear();
        this.StatusSyncs.Clear();
        this.StatusEntries.Clear();
        this.TraderStocks.Clear();
        this.PlayerDeaths.Clear();
        this.OxygenSyncs.Clear();
        this.SkillCasts.Clear();
        this.AttackPhases.Clear();
        this.EnhancementAnnouncements.Clear();
        this.Recipients.Clear();
    }
}

/// <summary>
/// Double-buffered outbound replication queue (audit SOLID-1, extracted from <see cref="AuthoritativeWorld"/>). The tick
/// stages messages into <see cref="Pending"/> under the world lock; <see cref="Swap"/> hands them to the dispatch side,
/// and <see cref="Dispatch"/> sends them through <see cref="IWorldOutboundSink"/> after the lock is released, in a
/// fixed order. No allocations in steady state.
/// </summary>
internal sealed class OutboundTickBuffer
{
    private OutboundBatch _pending = new();
    private OutboundBatch _dispatching = new();

    /// <summary>Gets the batch the current tick writes into (only under the world lock).</summary>
    public OutboundBatch Pending => _pending;

    /// <summary>Hands the staged batch to the dispatch side and starts an empty one. Call under the world lock.</summary>
    public void Swap()
    {
        (_pending, _dispatching) = (_dispatching, _pending);
        _pending.Clear();
    }

    /// <summary>Sends the swapped batch through <paramref name="sink"/>, then clears it. Call after the world lock is released.</summary>
    public void Dispatch(IWorldOutboundSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        OutboundBatch batch = _dispatching;

        for (int i = 0; i < batch.Spawns.Count; i++)
        {
            OutboundSpawn item = batch.Spawns[i];
            EntitySpawnSnapshot snap = item.Snapshot;
            sink.SendEntitySpawn(item.ConnectionId, in snap);
        }

        for (int i = 0; i < batch.Despawns.Count; i++)
        {
            OutboundDespawn item = batch.Despawns[i];
            sink.SendEntityDespawn(item.ConnectionId, item.NetworkId, item.ServerTick);
        }

        Span<EntityDeltaEntry> deltaSpan = CollectionsMarshal.AsSpan(batch.DeltaEntries);
        for (int i = 0; i < batch.Deltas.Count; i++)
        {
            OutboundDelta item = batch.Deltas[i];
            sink.SendEntityDeltaBatch(
                item.ConnectionId,
                item.ServerTick,
                item.LastClientTick,
                item.SelfX,
                item.SelfY,
                deltaSpan.Slice(item.Offset, item.Count));
        }

        Span<ulong> recipientsSpan = CollectionsMarshal.AsSpan(batch.Recipients);
        for (int i = 0; i < batch.CombatHits.Count; i++)
        {
            OutboundCombatHit hit = batch.CombatHits[i];
            sink.SendCombatHit(
                recipientsSpan.Slice(hit.RecipientsOffset, hit.RecipientsCount),
                hit.AttackerNetworkId,
                hit.DefenderNetworkId,
                hit.Damage,
                hit.RemainingHealth,
                hit.IsCritical,
                hit.IsKilled,
                hit.StatusEffectId,
                hit.StatusEffectLevel);
        }

        for (int i = 0; i < batch.SlotChanges.Count; i++)
        {
            OutboundSlotChange change = batch.SlotChanges[i];
            ItemSlot slotCopy = change.Slot;
            sink.SendInventorySlotChanged(
                change.ConnectionId,
                change.SlotIndex,
                slotCopy.ItemId,
                slotCopy.Quantity,
                slotCopy.Durability,
                ItemEnhancementPolicy.GetUpgradeLevel(in slotCopy),
                slotCopy.FreshnessSeconds);
        }

        // After the slot changes, so the client already holds the emptied slot when it shows the break (ADR-035).
        for (int i = 0; i < batch.DurabilityAlerts.Count; i++)
        {
            OutboundDurabilityAlert alert = batch.DurabilityAlerts[i];
            sink.SendItemDurabilityAlert(alert.ConnectionId, alert.Kind, alert.SlotIndex, alert.ItemId, alert.UpgradeLevel);
        }

        for (int i = 0; i < batch.PropChanges.Count; i++)
        {
            OutboundPropChange prop = batch.PropChanges[i];
            sink.SendPropChanged(
                recipientsSpan.Slice(prop.RecipientsOffset, prop.RecipientsCount),
                prop.TileX,
                prop.TileY,
                prop.Revision,
                prop.Durability,
                prop.IsDestroyed);
        }

        for (int i = 0; i < batch.StructureChanges.Count; i++)
        {
            OutboundStructureChange st = batch.StructureChanges[i];
            sink.SendStructureChanged(
                recipientsSpan.Slice(st.RecipientsOffset, st.RecipientsCount),
                st.TileX,
                st.TileY,
                st.StructureType,
                st.Durability,
                st.DoorState,
                st.Connectivity);
        }

        for (int i = 0; i < batch.EquipmentChanges.Count; i++)
        {
            OutboundEquipmentChange eq = batch.EquipmentChanges[i];
            sink.SendEntityEquipmentChanged(
                recipientsSpan.Slice(eq.RecipientsOffset, eq.RecipientsCount),
                eq.NetworkId,
                eq.HeadId,
                eq.ChestId,
                eq.LegsId,
                eq.FeetId,
                eq.WeaponId,
                eq.WeaponUpgrade,
                eq.Level,
                eq.MaxHealth);
        }

        for (int i = 0; i < batch.StatsSyncs.Count; i++)
        {
            OutboundStatsSync sync = batch.StatsSyncs[i];
            PlayerStatsSnapshot statsSnap = sync.Snapshot;
            sink.SendPlayerStatsSync(sync.ConnectionId, in statsSnap);
        }

        for (int i = 0; i < batch.StaminaStatuses.Count; i++)
        {
            OutboundStaminaStatus stamina = batch.StaminaStatuses[i];
            sink.SendStaminaStatus(stamina.ConnectionId, stamina.Exhausted, stamina.RefusalCount);
        }

        for (int i = 0; i < batch.WalletSyncs.Count; i++)
        {
            OutboundWalletSync wallet = batch.WalletSyncs[i];
            sink.SendWalletSync(wallet.ConnectionId, wallet.Gold, wallet.Diamonds, wallet.Sequence);
        }

        Span<StatusEffectEntry> statusSpan = CollectionsMarshal.AsSpan(batch.StatusEntries);
        for (int i = 0; i < batch.StatusSyncs.Count; i++)
        {
            OutboundStatusSync status = batch.StatusSyncs[i];
            sink.SendStatusEffects(status.ConnectionId, statusSpan.Slice(status.Offset, status.Count));
        }

        for (int i = 0; i < batch.OxygenSyncs.Count; i++)
        {
            OutboundOxygenSync oxygen = batch.OxygenSyncs[i];
            PlayerOxygenSnapshot snapshot = oxygen.Oxygen;
            sink.SendPlayerOxygen(oxygen.ConnectionId, in snapshot);
        }

        for (int i = 0; i < batch.TraderStocks.Count; i++)
        {
            OutboundTraderStock trader = batch.TraderStocks[i];
            sink.SendTraderStock(trader.ConnectionId, trader.Stock);
        }

        for (int i = 0; i < batch.PlayerDeaths.Count; i++)
        {
            OutboundPlayerDeath outbound = batch.PlayerDeaths[i];
            PlayerDeathSnapshot death = outbound.Death;
            sink.SendPlayerDeath(outbound.ConnectionId, in death);
        }

        for (int i = 0; i < batch.EnhancementAnnouncements.Count; i++)
        {
            OutboundEnhancementAnnouncement notice = batch.EnhancementAnnouncements[i];
            sink.SendEnhancementAnnouncement(
                recipientsSpan.Slice(notice.RecipientsOffset, notice.RecipientsCount),
                notice.PlayerNetworkId,
                notice.PlayerName,
                notice.ItemId,
                notice.Outcome,
                notice.Level);
        }

        for (int i = 0; i < batch.SkillCasts.Count; i++)
        {
            OutboundSkillCast cast = batch.SkillCasts[i];
            sink.SendSkillCastBroadcast(
                recipientsSpan.Slice(cast.RecipientsOffset, cast.RecipientsCount),
                cast.CasterNetworkId,
                cast.AbilitySlot,
                cast.OriginX,
                cast.OriginY,
                cast.AimX,
                cast.AimY);
        }

        for (int i = 0; i < batch.AttackPhases.Count; i++)
        {
            OutboundAttackPhase phase = batch.AttackPhases[i];
            AttackPhaseSnapshot snapshot = phase.Snapshot;
            sink.SendEntityAttackPhase(recipientsSpan.Slice(phase.RecipientsOffset, phase.RecipientsCount), in snapshot);
        }

        for (int i = 0; i < batch.ConstructionChanges.Count; i++)
        {
            OutboundConstructionChange cc = batch.ConstructionChanges[i];
            sink.SendConstructionChanged(
                recipientsSpan.Slice(cc.RecipientsOffset, cc.RecipientsCount),
                cc.TileX,
                cc.TileY,
                cc.StructureType,
                cc.Rotation,
                cc.Stage,
                cc.Size,
                cc.Progress,
                cc.IsDeconstruction);
        }

        batch.Clear();
    }
}
