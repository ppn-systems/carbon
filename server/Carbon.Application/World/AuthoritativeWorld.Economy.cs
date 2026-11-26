// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Application.World.Players;
using Carbon.Application.World.Commands;
using Carbon.Application.World.Commerce;
using Carbon.Contracts.Common;
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
    public ValueTask<WorldMutationOutcome> SubmitVendorBuyAsync(ulong connectionId, ushort itemId, ushort quantity) =>
        _commands.Submit(WorldCommandKind.VendorBuy, connectionId, itemId, quantity);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitVendorSellAsync(ulong connectionId, ushort slotIndex, ushort quantity) =>
        _commands.Submit(WorldCommandKind.VendorSell, connectionId, slotIndex, quantity);

    /// <inheritdoc/>
    public ValueTask<WorldMutationOutcome> SubmitRepairItemAsync(ulong connectionId, ushort slotIndex) =>
        _commands.Submit(WorldCommandKind.RepairItem, connectionId, slotIndex);

    /// <summary>Buys vendor-sold items for gold near a friendly town NPC. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryVendorBuy(ulong connectionId, ushort itemId, ushort quantity) =>
        quantity is 0 or > EconomyService.MaxVendorQuantity
            ? OpStatus.Invalid
            : this.RunVendorOperation(connectionId, VendorOperation.Buy, itemId, quantity);

    /// <summary>Sells units from a bag slot to a nearby friendly town NPC for gold. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryVendorSell(ulong connectionId, ushort slotIndex, ushort quantity) =>
        quantity == 0 || slotIndex >= PlayerInventoryLayout.BagLimit
            ? OpStatus.Invalid
            : this.RunVendorOperation(connectionId, VendorOperation.Sell, slotIndex, quantity);

    /// <summary>Repairs the item in the slot to full durability for gold at a forge station. Synchronous form used by the tick and tests; handlers use the matching <c>Submit*Async</c> (ADR-017).</summary>
    public OpStatus TryRepairItem(ulong connectionId, ushort slotIndex) =>
        this.RunVendorOperation(connectionId, VendorOperation.Repair, slotIndex, 0);

    /// <summary>
    /// Runs a vendor/repair mutation under the world lock, then pushes the changed inventory slots and the new wallet
    /// balance to the owning client.
    /// </summary>
    private OpStatus RunVendorOperation(ulong connectionId, VendorOperation operation, ushort arg, ushort quantity)
    {
        if (!_commerce.IsEnabled)
        {
            return OpStatus.Forbidden;
        }

        (ushort SlotIndex, ItemSlot Slot)[] modifiedCopy;
        long gold;
        long diamonds;
        ulong sequence;
        lock (_gate)
        {
            if (!_players.ByConnection.TryGetValue(connectionId, out PlayerState? state) ||
                !_simulation.Entities.HasTransform(state.Entity))
            {
                return OpStatus.Forbidden;
            }

            Vector2 playerPos = _simulation.Entities.GetTransform(state.Entity).Position;
            OpStatus status = _commerce.RunVendorOperation(state, playerPos, operation, arg, quantity, _mutationSlotScratch);
            if (status != OpStatus.Ok)
            {
                return status;
            }

            this.MarkCharacterDirtyLocked(state, inventoryChanged: true);
            modifiedCopy = [.. _mutationSlotScratch];
            gold = state.Wallet.Gold;
            diamonds = state.Wallet.Diamonds;
            sequence = state.Wallet.Sequence;
        }

        this.SendSlotsOutbound(connectionId, modifiedCopy);
        _outboundSink.SendWalletSync(connectionId, gold, diamonds, sequence);
        return OpStatus.Ok;
    }
}
