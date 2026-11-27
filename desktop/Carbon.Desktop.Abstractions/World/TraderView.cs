// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// The trader window's data as last pushed by the server (<c>TraderStock</c>, ADR-025). Prices, stock and sell prices
/// are the server's; the client only shows them and counts the restock timer down from <see cref="RestockAtUtc"/>.
/// </summary>
/// <param name="TraderNetworkId">Trader NPC network id.</param>
/// <param name="Kind">Trader kind (<c>TraderStock.Kind*</c>).</param>
/// <param name="Epoch">Restock epoch the stock belongs to (echoed on purchases).</param>
/// <param name="RestockAtUtc">Local wall-clock time of the next restock.</param>
/// <param name="ItemIds">Item per stock slot.</param>
/// <param name="UnitPrices">Gold per unit per stock slot.</param>
/// <param name="Stocked">Units stocked per slot.</param>
/// <param name="Remaining">Units left per slot.</param>
/// <param name="SellItemIds">Items the player carries that the trader buys.</param>
/// <param name="SellPrices">Gold per unit the trader pays.</param>
/// <param name="CanRepair">Whether the trader repairs gear (blacksmith).</param>
/// <param name="RepairSlots">Bag slots holding worn gear the trader can repair.</param>
/// <param name="RepairCosts">Gold to fully repair each of <paramref name="RepairSlots"/> (the server's quote).</param>
public sealed record TraderView(
    ulong TraderNetworkId,
    byte Kind,
    long Epoch,
    DateTimeOffset RestockAtUtc,
    ushort[] ItemIds,
    int[] UnitPrices,
    ushort[] Stocked,
    ushort[] Remaining,
    ushort[] SellItemIds,
    int[] SellPrices,
    bool CanRepair,
    ushort[] RepairSlots,
    int[] RepairCosts)
{
    /// <summary>Gets the server's quote to fully repair the gear in <paramref name="bagSlot"/>, or 0 when it is not worn.</summary>
    public int RepairCostOf(int bagSlot)
    {
        int index = Array.IndexOf(this.RepairSlots, (ushort)bagSlot);
        return index >= 0 ? this.RepairCosts[index] : 0;
    }

    /// <summary>Gets what the trader pays per unit of <paramref name="itemId"/>, or 0 when it does not buy it.</summary>
    public int SellPriceOf(uint itemId)
    {
        int index = Array.IndexOf(this.SellItemIds, (ushort)itemId);
        return index >= 0 ? this.SellPrices[index] : 0;
    }

    /// <summary>Gets the whole seconds until the restock, from <paramref name="now"/> (never negative).</summary>
    public int SecondsUntilRestock(DateTimeOffset now) => (int)Math.Max(0, Math.Ceiling((this.RestockAtUtc - now).TotalSeconds));
}
