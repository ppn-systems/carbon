// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Carbon.Contracts.Common;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;

namespace Carbon.Items.Repair;

/// <summary>
/// The validated repair rules compiled from <c>repair.json</c> (ADR-028): the material family of every item with
/// durability, the step size, the tier multiplier, the station tiers and the blacksmith fee. Immutable; the server and
/// each synced client hold one per <see cref="ItemRules"/>, so the repair window shows exactly what the server spends.
/// </summary>
public sealed class RepairTable
{
    private const long FeeDivisor = 100L * 100L * 1000L;

    private readonly FrozenDictionary<uint, Entry> _items;
    private readonly int[] _tierPercent;
    private readonly RarityPercents _feeRarity;

    private RepairTable(
        FrozenDictionary<uint, Entry> items,
        RepairFamily[] families,
        int[] tierPercent,
        int stepPercent,
        RepairStationTiers stations,
        RepairFeeDefinition fee,
        DurabilityWarningDefinition warning)
    {
        _items = items;
        _tierPercent = tierPercent;
        _feeRarity = fee.RarityPercent;
        this.Families = families;
        this.StepPercent = stepPercent;
        this.WorkbenchTier = stations.Workbench;
        this.ForgeAnvilTier = stations.ForgeAnvil;
        this.BlacksmithTier = stations.Blacksmith;
        this.StationReachTiles = stations.StationReachTiles;
        this.BlacksmithReach = stations.BlacksmithReach;
        this.FeePercentOfBasePrice = fee.PercentOfBasePrice;
        this.MinimumFee = fee.Minimum;
        this.WarningLowPercent = warning.LowPercent;
        this.WarningCriticalPercent = warning.CriticalPercent;
    }

    /// <summary>Gets the durability one step restores, in percent of the item's maximum.</summary>
    public int StepPercent { get; }

    /// <summary>Gets the steps of a full repair (0 → max).</summary>
    public int StepsPerFullRepair => 100 / this.StepPercent;

    /// <summary>Gets the tier a workbench provides.</summary>
    public int WorkbenchTier { get; }

    /// <summary>Gets the tier a forge anvil provides.</summary>
    public int ForgeAnvilTier { get; }

    /// <summary>Gets the tier a blacksmith NPC provides.</summary>
    public int BlacksmithTier { get; }

    /// <summary>Gets how many tiles away a workbench or forge anvil still repairs.</summary>
    public int StationReachTiles { get; }

    /// <summary>Gets how many world pixels away a blacksmith still repairs.</summary>
    public float BlacksmithReach { get; }

    /// <summary>Gets the blacksmith fee of a full repair, in percent of the base price (before rarity).</summary>
    public int FeePercentOfBasePrice { get; }

    /// <summary>Gets the smallest blacksmith fee.</summary>
    public long MinimumFee { get; }

    /// <summary>Gets every family.</summary>
    public IReadOnlyList<RepairFamily> Families { get; }

    /// <summary>Gets the durability share (percent of max) at or below which the bar turns yellow (ADR-035).</summary>
    public int WarningLowPercent { get; }

    /// <summary>Gets the durability share (percent of max) at or below which the bar turns red and the owner is warned once (ADR-035).</summary>
    public int WarningCriticalPercent { get; }

    /// <summary>Classifies <paramref name="durability"/> out of <paramref name="maxDurability"/> against the warning shares (ADR-035).</summary>
    /// <param name="durability">Current durability.</param>
    /// <param name="maxDurability">Effective maximum (enhancement included); an item without durability is always healthy.</param>
    /// <returns>The band the durability bar is drawn in.</returns>
    public DurabilityBand GetDurabilityBand(float durability, float maxDurability)
    {
        if (maxDurability <= 0f)
        {
            return DurabilityBand.Healthy;
        }

        float percent = durability * 100f / maxDurability;
        return percent <= this.WarningCriticalPercent
            ? DurabilityBand.Critical
            : percent <= this.WarningLowPercent ? DurabilityBand.Low : DurabilityBand.Healthy;
    }

    /// <summary>Parses, validates and compiles a <c>repair.json</c> document.</summary>
    /// <param name="json">Document text.</param>
    /// <param name="items">Item catalog used to resolve item and material codes.</param>
    /// <returns>The compiled table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the document is malformed or inconsistent.</exception>
    public static RepairTable LoadFromJson(string json, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(items);
        RepairDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, ItemsJsonContext.Default.RepairDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"repair.json is malformed: {ex.Message}", ex);
        }

        return Compile(document ?? throw new InvalidDataException("repair.json is empty."), items);
    }

    /// <summary>Validates and compiles <paramref name="document"/>.</summary>
    /// <param name="document">Parsed document.</param>
    /// <param name="items">Item catalog used to resolve codes.</param>
    /// <returns>The compiled table.</returns>
    /// <exception cref="InvalidDataException">Thrown when the document is inconsistent.</exception>
    public static RepairTable Compile(RepairDocument document, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(items);

        if (document.StepPercent is < 1 or > 100 || 100 % document.StepPercent != 0)
        {
            throw new InvalidDataException("repair.json stepPercent must divide 100.");
        }

        RepairStationTiers stations = document.Stations ?? new RepairStationTiers();
        if (stations.Workbench < 1 || stations.ForgeAnvil < stations.Workbench || stations.Blacksmith < stations.ForgeAnvil)
        {
            throw new InvalidDataException("repair.json needs 1 <= stations.workbench <= stations.forgeAnvil <= stations.blacksmith.");
        }

        if (stations.StationReachTiles < 1 || stations.BlacksmithReach <= 0f)
        {
            throw new InvalidDataException("repair.json stations.stationReachTiles and stations.blacksmithReach must be positive.");
        }

        DurabilityWarningDefinition warning = document.DurabilityWarning ?? new DurabilityWarningDefinition();
        if (warning.CriticalPercent < 1 || warning.LowPercent <= warning.CriticalPercent || warning.LowPercent >= 100)
        {
            throw new InvalidDataException("repair.json needs 1 <= durabilityWarning.criticalPercent < durabilityWarning.lowPercent < 100.");
        }

        RepairFeeDefinition fee = document.BlacksmithFee ?? new RepairFeeDefinition();
        RarityPercents rarity = fee.RarityPercent ?? new RarityPercents();
        if (fee.PercentOfBasePrice < 0 || fee.Minimum < 0 || !rarity.IsValid)
        {
            throw new InvalidDataException("repair.json blacksmithFee values must not be negative.");
        }

        int[] tierPercent = [.. document.TierPercent ?? []];
        for (int i = 0; i < tierPercent.Length; i++)
        {
            if (tierPercent[i] < 1)
            {
                throw new InvalidDataException("repair.json tierPercent values must be >= 1.");
            }
        }

        IReadOnlyList<RepairFamilyDefinition> defs = document.Families ?? [];
        RepairFamily[] families = new RepairFamily[defs.Count];
        Dictionary<uint, Entry> byItem = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int f = 0; f < defs.Count; f++)
        {
            RepairFamilyDefinition def = defs[f];
            if (string.IsNullOrWhiteSpace(def.Id) || !ids.Add(def.Id))
            {
                throw new InvalidDataException($"repair.json family id '{def.Id}' is empty or repeated.");
            }

            if (def.StationTier < stations.Workbench || def.StationTier > stations.Blacksmith)
            {
                throw new InvalidDataException($"repair.json family '{def.Id}' stationTier must be a station's tier.");
            }

            IReadOnlyList<RecipeItemAmount> materialDefs = def.Materials ?? [];
            if (materialDefs.Count == 0)
            {
                throw new InvalidDataException($"repair.json family '{def.Id}' needs at least one material.");
            }

            RecipeIngredient[] materials = new RecipeIngredient[materialDefs.Count];
            for (int m = 0; m < materials.Length; m++)
            {
                if (!items.TryGetByCode(materialDefs[m].Item, out ItemCatalogEntry? material) || material.MaxDurability > 0f || materialDefs[m].Qty < 1)
                {
                    throw new InvalidDataException($"repair.json family '{def.Id}' material '{materialDefs[m].Item}' must be a known item without durability and qty >= 1.");
                }

                materials[m] = new RecipeIngredient(material.Id, materialDefs[m].Qty);
            }

            RepairFamily family = new(def.Id, def.StationTier, materials);
            families[f] = family;
            foreach (string code in def.Items ?? [])
            {
                if (!items.TryGetByCode(code, out ItemCatalogEntry? item) || item.MaxDurability <= 0f)
                {
                    throw new InvalidDataException($"repair.json family '{def.Id}' item '{code}' must be a known item with durability.");
                }

                if (item.Tier < 0 || item.Tier >= tierPercent.Length)
                {
                    throw new InvalidDataException($"repair.json tierPercent has no entry for tier {item.Tier} ('{code}').");
                }

                if (!byItem.TryAdd(item.Id, new Entry(family, item)))
                {
                    throw new InvalidDataException($"repair.json lists item '{code}' in more than one family.");
                }
            }
        }

        foreach (ItemCatalogEntry item in items.All)
        {
            if (item.MaxDurability > 0f && !byItem.ContainsKey(item.Id))
            {
                throw new InvalidDataException($"repair.json has no family for '{item.Code}', which has durability.");
            }
        }

        return new RepairTable(byItem.ToFrozenDictionary(), families, tierPercent, document.StepPercent, stations, fee, warning);
    }

    /// <summary>Gets the family of <paramref name="itemId"/>.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="family">The family when the item is repairable.</param>
    /// <returns><see langword="true"/> for an item with durability.</returns>
    public bool TryGetFamily(uint itemId, out RepairFamily? family)
    {
        bool found = _items.TryGetValue(itemId, out Entry entry);
        family = found ? entry.Family : null;
        return found;
    }

    /// <summary>
    /// Quotes <paramref name="steps"/> repair steps of <paramref name="slot"/> at <paramref name="venue"/>. Checks the
    /// item only; the caller checks the station tier, materials and gold.
    /// </summary>
    /// <param name="slot">The item.</param>
    /// <param name="steps">Steps to buy (<c>1..</c><see cref="RepairQuote.StepsNeeded"/>).</param>
    /// <param name="venue">Where the repair happens (only <see cref="RepairVenue.Blacksmith"/> charges gold).</param>
    /// <param name="quote">The quote on success.</param>
    /// <returns><see cref="RepairError.None"/>, or why the item cannot be repaired that way.</returns>
    public RepairError TryQuote(in ItemSlot slot, int steps, RepairVenue venue, out RepairQuote quote)
    {
        quote = default;
        if (venue is not (RepairVenue.Station or RepairVenue.Blacksmith))
        {
            return RepairError.InvalidRequest;
        }

        if (slot.IsEmpty)
        {
            return RepairError.EmptySlot;
        }

        // ADR-035: an item is repairable only while it still has durability; at 0 it no longer exists.
        if (!_items.TryGetValue(slot.ItemId, out Entry entry) || slot.Durability <= 0f)
        {
            return RepairError.NotRepairable;
        }

        float max = ItemEnhancementPolicy.GetEffectiveMaxDurability(entry.Item.MaxDurability, ItemEnhancementPolicy.GetUpgradeLevel(in slot));
        float current = Math.Clamp(slot.Durability, 0f, max);
        float missing = max - current;
        if (missing <= 0f)
        {
            return RepairError.NotDamaged;
        }

        int stepPermille = this.StepPercent * 10;
        int missingPermille = (int)Math.Clamp(MathF.Ceiling(missing * 1000f / max), 1f, 1000f);
        int stepsNeeded = (missingPermille + stepPermille - 1) / stepPermille;
        if (steps < 1)
        {
            return RepairError.InvalidRequest;
        }

        if (steps > stepsNeeded)
        {
            return RepairError.TooManySteps;
        }

        // A full repair restores exactly what is missing; a partial one restores whole steps.
        float restored = steps == stepsNeeded ? missing : Math.Min(missing, max * steps * this.StepPercent / 100f);
        long fee = venue == RepairVenue.Blacksmith ? this.GetFee(entry.Item, restored, max) : 0L;
        quote = new RepairQuote(
            entry.Family,
            slot.ItemId,
            venue,
            current,
            max,
            stepsNeeded,
            steps,
            restored,
            fee,
            _tierPercent[entry.Item.Tier] * steps,
            100 * this.StepsPerFullRepair);
        return RepairError.None;
    }

    /// <summary>Quotes a full repair of <paramref name="slot"/> at <paramref name="venue"/>.</summary>
    /// <param name="slot">The item.</param>
    /// <param name="venue">Where the repair happens.</param>
    /// <param name="quote">The quote on success.</param>
    /// <returns><see cref="RepairError.None"/>, or why the item cannot be repaired.</returns>
    public RepairError TryQuoteFull(in ItemSlot slot, RepairVenue venue, out RepairQuote quote)
    {
        RepairError probe = this.TryQuote(in slot, 1, venue, out quote);
        return probe != RepairError.None || quote.StepsNeeded == 1 ? probe : this.TryQuote(in slot, quote.StepsNeeded, venue, out quote);
    }

    /// <summary>
    /// Returns the most steps of <paramref name="slot"/> that <paramref name="slots"/> and <paramref name="gold"/> pay
    /// for (0 when not even one): the "repair what I can" choice of a partial repair (ADR-028 D4).
    /// </summary>
    /// <param name="slot">The item.</param>
    /// <param name="venue">Where the repair happens.</param>
    /// <param name="slots">The player's inventory slots.</param>
    /// <param name="gold">The player's gold.</param>
    /// <returns>The affordable step count.</returns>
    public int GetAffordableSteps(in ItemSlot slot, RepairVenue venue, ReadOnlySpan<ItemSlot> slots, long gold)
    {
        if (this.TryQuote(in slot, 1, venue, out RepairQuote probe) != RepairError.None)
        {
            return 0;
        }

        for (int steps = probe.StepsNeeded; steps >= 1; steps--)
        {
            if (this.TryQuote(in slot, steps, venue, out RepairQuote quote) == RepairError.None &&
                quote.GoldFee <= gold && HasMaterials(in quote, slots))
            {
                return steps;
            }
        }

        return 0;
    }

    /// <summary>Returns whether <paramref name="slots"/> hold every material of <paramref name="quote"/>.</summary>
    /// <param name="quote">The quote.</param>
    /// <param name="slots">The player's inventory slots.</param>
    /// <returns><see langword="true"/> when nothing is missing.</returns>
    public static bool HasMaterials(in RepairQuote quote, ReadOnlySpan<ItemSlot> slots)
    {
        for (int i = 0; i < quote.MaterialCount; i++)
        {
            RecipeIngredient material = quote.GetMaterial(i);
            if (CountItem(slots, material.ItemId) < material.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Counts <paramref name="itemId"/> across <paramref name="slots"/>.</summary>
    /// <param name="slots">Inventory slots.</param>
    /// <param name="itemId">Item id.</param>
    /// <returns>The total quantity.</returns>
    public static int CountItem(ReadOnlySpan<ItemSlot> slots, uint itemId)
    {
        int total = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].ItemId == itemId)
            {
                total += slots[i].Quantity;
            }
        }

        return total;
    }

    private long GetFee(ItemCatalogEntry item, float restored, float max)
    {
        int rarity = _feeRarity.For(item.Rarity);
        long restoredPermille = (long)Math.Clamp(MathF.Ceiling(restored * 1000f / max), 1f, 1000f);
        long numerator = item.BasePrice * this.FeePercentOfBasePrice * rarity * restoredPermille;
        long fee = (numerator + FeeDivisor - 1) / FeeDivisor;
        return Math.Max(this.MinimumFee, fee);
    }

    private readonly record struct Entry(RepairFamily Family, ItemCatalogEntry Item);
}
