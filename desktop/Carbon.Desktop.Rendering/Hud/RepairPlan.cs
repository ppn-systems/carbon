// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Repair;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>What one row of the repair window's requirement list checks (ADR-028).</summary>
internal enum RepairRequirementKind : byte
{
    /// <summary>A material: <see cref="RepairRequirement.ItemId"/>, quantity needed and carried.</summary>
    Material = 0,

    /// <summary>The blacksmith's gold fee and the player's gold.</summary>
    Gold = 1,

    /// <summary>The station tier the item needs and the tier in reach.</summary>
    Station = 2,
}

/// <summary>One requirement row: what is needed and what the player has. Missing rows are drawn in red.</summary>
/// <param name="Kind">What the row checks.</param>
/// <param name="ItemId">Material item id (materials only).</param>
/// <param name="Need">Quantity, gold or tier needed.</param>
/// <param name="Have">Quantity carried, gold owned or tier in reach.</param>
internal readonly record struct RepairRequirement(RepairRequirementKind Kind, uint ItemId, long Need, long Have)
{
    /// <summary>Gets whether the player meets the requirement.</summary>
    public bool IsMet => this.Have >= this.Need;
}

/// <summary>
/// The repair window's view of the selected item (ADR-028), built from the synced <c>repair.json</c>
/// (<see cref="ItemRules.Repair"/>): durability now and after, the restore amount, and the requirement rows. It only
/// shows the server's rules; the server checks and spends.
/// </summary>
internal readonly struct RepairPlan
{
    /// <summary>The most requirement rows a plan writes: materials, the gold fee and the station.</summary>
    public const int MaxRequirements = 6;

    /// <summary>Gets whether an item is selected and can be repaired at all.</summary>
    public bool HasItem { get; init; }

    /// <summary>Gets the selected slot.</summary>
    public int SlotIndex { get; init; }

    /// <summary>Gets the selected item.</summary>
    public uint ItemId { get; init; }

    /// <summary>Gets the quote of the planned repair (full, or the partial one when <see cref="IsPartial"/>).</summary>
    public RepairQuote Quote { get; init; }

    /// <summary>Gets whether the plan is a partial repair.</summary>
    public bool IsPartial { get; init; }

    /// <summary>Gets the most steps the player can pay for (0: not even one).</summary>
    public int AffordableSteps { get; init; }

    /// <summary>Gets how many requirement rows were written.</summary>
    public int RequirementCount { get; init; }

    /// <summary>Gets whether every requirement row is met.</summary>
    public bool CanRepair { get; init; }

    /// <summary>
    /// Builds the plan for the item in <paramref name="slotIndex"/>: a full repair, or with <paramref name="partial"/>
    /// the largest affordable one. Writes the requirement rows to <paramref name="requirements"/>.
    /// </summary>
    /// <param name="table">Synced repair table.</param>
    /// <param name="slots">The player's inventory slots.</param>
    /// <param name="slotIndex">Selected slot (<c>-1</c>: none).</param>
    /// <param name="venue">Venue of the window.</param>
    /// <param name="stationTier">Tier of the venue in reach (<c>0</c>: none).</param>
    /// <param name="gold">The player's gold.</param>
    /// <param name="partial">Whether to plan the largest affordable partial repair.</param>
    /// <param name="requirements">At least <see cref="MaxRequirements"/> rows.</param>
    /// <returns>The plan (<see cref="HasItem"/> is <see langword="false"/> when nothing is selected).</returns>
    public static RepairPlan Build(
        RepairTable table,
        ReadOnlySpan<ItemSlot> slots,
        int slotIndex,
        RepairVenue venue,
        int stationTier,
        long gold,
        bool partial,
        Span<RepairRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(table);
        if ((uint)slotIndex >= (uint)slots.Length || table.TryQuoteFull(in slots[slotIndex], venue, out RepairQuote full) != RepairError.None)
        {
            return new RepairPlan { SlotIndex = -1 };
        }

        ItemSlot item = slots[slotIndex];
        int affordable = table.GetAffordableSteps(in item, venue, slots, gold);
        RepairQuote quote = full;
        bool isPartial = partial && affordable > 0 && affordable < full.StepsNeeded &&
            table.TryQuote(in item, affordable, venue, out quote) == RepairError.None;
        if (!isPartial)
        {
            quote = full;
        }

        int count = 0;
        bool met = true;
        for (int i = 0; i < quote.MaterialCount && count < MaxRequirements - 2; i++)
        {
            RecipeIngredient material = quote.GetMaterial(i);
            RepairRequirement row = new(RepairRequirementKind.Material, material.ItemId, material.Quantity, RepairTable.CountItem(slots, material.ItemId));
            requirements[count++] = row;
            met &= row.IsMet;
        }

        if (venue == RepairVenue.Blacksmith)
        {
            RepairRequirement fee = new(RepairRequirementKind.Gold, 0U, quote.GoldFee, gold);
            requirements[count++] = fee;
            met &= fee.IsMet;
        }

        RepairRequirement station = new(RepairRequirementKind.Station, 0U, quote.RequiredStationTier, stationTier);
        requirements[count++] = station;
        met &= station.IsMet;
        return new RepairPlan
        {
            HasItem = true,
            SlotIndex = slotIndex,
            ItemId = item.ItemId,
            Quote = quote,
            IsPartial = isPartial,
            AffordableSteps = affordable,
            RequirementCount = count,
            CanRepair = met,
        };
    }

    /// <summary>Lists the damaged, repairable items: equipped gear first, then the hotbar and backpack.</summary>
    /// <param name="table">Synced repair table.</param>
    /// <param name="slots">The player's inventory slots.</param>
    /// <param name="into">Cleared, then filled with slot indices.</param>
    public static void CollectDamaged(RepairTable table, ReadOnlySpan<ItemSlot> slots, List<ushort> into)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        for (int s = PlayerInventoryLayout.BagLimit; s < slots.Length; s++)
        {
            if (table.TryQuote(in slots[s], 1, RepairVenue.Station, out _) == RepairError.None)
            {
                into.Add((ushort)s);
            }
        }

        for (int s = 0; s < PlayerInventoryLayout.BagLimit && s < slots.Length; s++)
        {
            if (table.TryQuote(in slots[s], 1, RepairVenue.Station, out _) == RepairError.None)
            {
                into.Add((ushort)s);
            }
        }
    }
}
