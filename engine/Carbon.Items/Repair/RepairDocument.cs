// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Items.Crafting;

namespace Carbon.Items.Repair;

/// <summary>JSON shape of <c>resources/config/repair.json</c> (ADR-028). Compiled by <see cref="RepairTable"/>.</summary>
public sealed record RepairDocument
{
    /// <summary>Gets or sets the schema version.</summary>
    public int Version { get; set; }

    /// <summary>Gets or sets the durability one repair step restores, in percent of the item's maximum (divides 100).</summary>
    public int StepPercent { get; set; }

    /// <summary>Gets or sets the material multiplier per item tier, in percent, indexed by <c>items.json</c> tier.</summary>
    public IReadOnlyList<int> TierPercent { get; set; } = [];

    /// <summary>Gets or sets the tier of each repair venue.</summary>
    public RepairStationTiers Stations { get; set; } = new();

    /// <summary>Gets or sets the blacksmith's gold fee.</summary>
    public RepairFeeDefinition BlacksmithFee { get; set; } = new();

    /// <summary>Gets or sets the material families; every item with durability belongs to exactly one.</summary>
    public IReadOnlyList<RepairFamilyDefinition> Families { get; set; } = [];

    /// <summary>Gets or sets when a durability bar warns before the item breaks (ADR-035).</summary>
    public DurabilityWarningDefinition DurabilityWarning { get; set; } = new();
}

/// <summary>
/// The durability shares (in percent of the item's maximum) at which the client colours the durability bar and the
/// server sends the one-time "about to break" alert (ADR-035).
/// </summary>
public sealed record DurabilityWarningDefinition
{
    /// <summary>Gets or sets the share at or below which the bar turns yellow.</summary>
    public int LowPercent { get; set; }

    /// <summary>Gets or sets the share at or below which the bar turns red and the owner is warned once.</summary>
    public int CriticalPercent { get; set; }
}

/// <summary>The tier each repair venue provides.</summary>
public sealed record RepairStationTiers
{
    /// <summary>Gets or sets the workbench tier.</summary>
    public int Workbench { get; set; }

    /// <summary>Gets or sets the forge anvil tier.</summary>
    public int ForgeAnvil { get; set; }

    /// <summary>Gets or sets the blacksmith NPC tier.</summary>
    public int Blacksmith { get; set; }

    /// <summary>Gets or sets how many tiles away a workbench or forge anvil still repairs.</summary>
    public int StationReachTiles { get; set; }

    /// <summary>Gets or sets how many world pixels away a blacksmith still repairs.</summary>
    public float BlacksmithReach { get; set; }
}

/// <summary>The blacksmith's gold fee: <c>basePrice × percentOfBasePrice% × rarity% × restored share</c>, at least <see cref="Minimum"/>.</summary>
public sealed record RepairFeeDefinition
{
    /// <summary>Gets or sets the fee of a full repair (0 → max durability) in percent of the item's base price.</summary>
    public int PercentOfBasePrice { get; set; }

    /// <summary>Gets or sets the smallest fee of any repair.</summary>
    public long Minimum { get; set; }

    /// <summary>Gets or sets the fee multiplier per rarity, in percent.</summary>
    public RepairRarityPercents RarityPercent { get; set; } = new();
}

/// <summary>Fee multiplier per rarity, in percent.</summary>
public sealed record RepairRarityPercents
{
    /// <summary>Gets or sets the common multiplier.</summary>
    public int Common { get; set; } = 100;

    /// <summary>Gets or sets the uncommon multiplier.</summary>
    public int Uncommon { get; set; } = 100;

    /// <summary>Gets or sets the rare multiplier.</summary>
    public int Rare { get; set; } = 100;

    /// <summary>Gets or sets the epic multiplier.</summary>
    public int Epic { get; set; } = 100;

    /// <summary>Gets or sets the legendary multiplier.</summary>
    public int Legendary { get; set; } = 100;
}

/// <summary>One <c>families[]</c> entry: the materials a group of items is repaired with.</summary>
public sealed record RepairFamilyDefinition
{
    /// <summary>Gets or sets the family id (for example <c>wood</c>, <c>iron</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the lowest station tier that can repair the family.</summary>
    public int StationTier { get; set; }

    /// <summary>Gets or sets the materials of a full repair (0 → max) of a tier-100 % item.</summary>
    public IReadOnlyList<RecipeItemAmount> Materials { get; set; } = [];

    /// <summary>Gets or sets the item codes of the family.</summary>
    public IReadOnlyList<string> Items { get; set; } = [];
}
