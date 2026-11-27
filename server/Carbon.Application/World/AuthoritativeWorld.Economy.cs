// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Application.World.Players;
using Carbon.Application.World.Commands;
using Carbon.Application.World.Commerce;
using Carbon.Contracts.Common;
using Carbon.Economy.Repair;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;

namespace Carbon.Application.World;

/// <summary>
/// Economy commands and queries; the rules live in <see cref="EconomyService"/>.
/// </summary>
public sealed partial class AuthoritativeWorld
{
    /// <summary>
    /// Gets the gold and diamond balances of the player on <paramref name="connectionId"/>.
    /// </summary>
    /// <param name="connectionId">Player connection.</param>
    /// <param name="gold">Gold balance.</param>
    /// <param name="diamonds">Diamond balance.</param>
    /// <returns><see langword="true"/> when the player is in the world.</returns>
    public bool TryGetWalletBalances(ulong connectionId, out long gold, out long diamonds)
    {
        lock (_gate)
        {
            if (_players.ByConnection.TryGetValue(connectionId, out PlayerState? state))
            {
                gold = state.Wallet.Gold;
                diamonds = state.Wallet.Diamonds;
                return true;
            }
        }

        gold = 0;
        diamonds = 0;
        return false;
    }

    /// <summary>
    /// Maps a <see cref="WorldActorCatalog"/> monster archetype to its <c>loot.json</c> table id.
    /// </summary>
    /// <param name="archetypeId">Monster archetype id.</param>
    /// <returns>The loot table id, or <see langword="null"/> for actors without one.</returns>
    public static string? ResolveLootTableId(ushort archetypeId) => EconomyService.ResolveLootTableId(archetypeId);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitRepairItemAsync(ulong connectionId, ushort slotIndex, ushort expectedItemId, byte steps, RepairVenue venue) =>
        _commands.Submit(WorldCommandKind.RepairItem, connectionId, slotIndex, expectedItemId, steps, (ushort)venue);

    /// <summary>
    /// Repairs ONE item for materials (and the blacksmith fee) at <paramref name="venue"/> (ADR-028). Returns
    /// <see cref="OpStatus.Ok"/> with a packed <see cref="RepairReceipt"/>; a refusal is <see cref="OpStatus.Conflict"/>
    /// (or <see cref="OpStatus.OutOfRange"/>) with the reason in the receipt, and nothing spent. Synchronous form used by
    /// the tick and tests; handlers use <see cref="SubmitRepairItemAsync"/> (ADR-017).
    /// </summary>
    public WorldMutationOutcome TryRepairItem(ulong connectionId, ushort slotIndex, ushort expectedItemId, byte steps, RepairVenue venue)
    {
        PlayerStatsSnapshot statsSnap;
        RepairAttempt attempt;
        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;
        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return new WorldMutationOutcome(OpStatus.Forbidden, 0U);
            }

            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            attempt = _inventory.Repair(state, slotIndex, expectedItemId, steps, venue, playerPos, _mutationSlotScratch);
            if (!attempt.IsOk)
            {
                OpStatus refused = attempt.Error == RepairError.OutOfRange ? OpStatus.OutOfRange : OpStatus.Conflict;
                return new WorldMutationOutcome(refused, new RepairReceipt(attempt.Error, 0).Pack());
            }

            this.RecomputeDerivedStatsLocked(state);
            statsSnap = this.BuildPlayerStatsSnapshotLocked(state, clearLevelUpPending: false);
            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            modifiedCopy = [.. _mutationSlotScratch];
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);
        _outboundSink.SendPlayerStatsSync(connectionId, in statsSnap);
        return new WorldMutationOutcome(OpStatus.Ok, new RepairReceipt(RepairError.None, (byte)attempt.Steps).Pack());
    }
}
