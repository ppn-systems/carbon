// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Economy.Currency;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Repair;

namespace Carbon.Economy.Repair;

/// <summary>
/// Runs one repair atomically (ADR-028): the item, the station tier, every material and the gold fee are checked
/// before anything is spent; then the fee and the materials are taken exactly once and only the selected slot's
/// durability changes. The caller holds the world lock, finds the station tier in reach, persists and replicates.
/// </summary>
public static class RepairWorkshop
{
    /// <summary>Repairs <paramref name="steps"/> steps of the item in <paramref name="slotIndex"/>.</summary>
    /// <param name="table">Repair table.</param>
    /// <param name="wallet">Wallet charged the blacksmith fee.</param>
    /// <param name="inventory">Inventory holding the item and the materials.</param>
    /// <param name="slotIndex">Slot of the item.</param>
    /// <param name="expectedItemId">Item the client saw in the slot (<c>0</c>: any).</param>
    /// <param name="steps">Steps to buy.</param>
    /// <param name="venue">Venue asked for.</param>
    /// <param name="stationTier">Tier of the venue in reach (<c>0</c>: none in reach).</param>
    /// <param name="tick">Tick recorded on the wallet ledger.</param>
    /// <returns>What happened.</returns>
    public static RepairAttempt TryRepair(
        RepairTable table,
        Wallet wallet,
        InventoryContainer inventory,
        int slotIndex,
        uint expectedItemId,
        int steps,
        RepairVenue venue,
        int stationTier,
        ulong tick = 0)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(inventory);
        if ((uint)slotIndex >= (uint)inventory.Capacity)
        {
            return RepairAttempt.Refused(RepairError.InvalidRequest);
        }

        ItemSlot target = inventory.GetSlot(slotIndex);
        if (!target.IsEmpty && expectedItemId != 0U && target.ItemId != expectedItemId)
        {
            return RepairAttempt.Refused(RepairError.ItemMismatch);
        }

        RepairError error = table.TryQuote(in target, steps, venue, out RepairQuote quote);
        if (error != RepairError.None)
        {
            return RepairAttempt.Refused(error);
        }

        if (stationTier <= 0)
        {
            return RepairAttempt.Refused(RepairError.OutOfRange);
        }

        if (stationTier < quote.RequiredStationTier)
        {
            return RepairAttempt.Refused(RepairError.StationTierTooLow);
        }

        // Every check before any spend, so a refusal changes nothing.
        if (!RepairTable.HasMaterials(in quote, inventory.Slots))
        {
            return RepairAttempt.Refused(RepairError.MissingMaterials);
        }

        if (!wallet.CanAfford(CurrencyKind.Gold, quote.GoldFee))
        {
            return RepairAttempt.Refused(RepairError.NotEnoughGold);
        }

        if (quote.GoldFee > 0 && !wallet.TrySpend(CurrencyKind.Gold, quote.GoldFee, CurrencyReason.Repair, tick).IsOk)
        {
            return RepairAttempt.Refused(RepairError.NotEnoughGold);
        }

        for (int i = 0; i < quote.MaterialCount; i++)
        {
            RecipeIngredient material = quote.GetMaterial(i);
            _ = inventory.TryRemove(material.ItemId, material.Quantity);
        }

        float durability = quote.NewDurability;
        inventory.SetSlot(slotIndex, target with { Durability = durability });
        return new RepairAttempt(RepairError.None, target.ItemId, quote.Steps, durability, quote.MaxDurability, quote.GoldFee);
    }
}
