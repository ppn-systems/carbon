// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Abstractions.World;

/// <summary>A trader's stock as one player sees it (mirrors the <c>TraderStock</c> packet, ADR-025).</summary>
/// <param name="TraderNetworkId">Trader NPC network id.</param>
/// <param name="Kind">Trader kind (<c>TraderStock.Kind*</c>).</param>
/// <param name="Epoch">Restock epoch.</param>
/// <param name="SecondsUntilRestock">Seconds until the next restock.</param>
/// <param name="ItemIds">Item per stock slot.</param>
/// <param name="UnitPrices">Gold per unit per stock slot.</param>
/// <param name="Stocked">Units stocked per slot.</param>
/// <param name="Remaining">Units left per slot.</param>
/// <param name="SellItemIds">Items the player carries that the trader buys.</param>
/// <param name="SellPrices">Gold per unit the trader pays.</param>
/// <param name="CanRepair">Whether the trader repairs gear.</param>
/// <param name="RepairSlots">Bag slots holding worn gear the trader can repair.</param>
/// <param name="RepairCosts">Gold to fully repair each of <paramref name="RepairSlots"/>.</param>
public sealed record TraderStockSnapshot(
    ulong TraderNetworkId,
    byte Kind,
    long Epoch,
    int SecondsUntilRestock,
    ushort[] ItemIds,
    int[] UnitPrices,
    ushort[] Stocked,
    ushort[] Remaining,
    ushort[] SellItemIds,
    int[] SellPrices,
    bool CanRepair,
    ushort[] RepairSlots,
    int[] RepairCosts);
