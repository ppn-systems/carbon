// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Items.Crafting;

namespace Carbon.Items.Enhancement;

/// <summary>JSON shape of <c>resources/config/enhancement.json</c> (ADR-020). Compiled by <see cref="EnhancementTable"/>.</summary>
public sealed record EnhancementDocument
{
    /// <summary>Gets or sets the schema version.</summary>
    public int Version { get; set; }

    /// <summary>Gets or sets the highest level the forge can reach (at most <see cref="Catalog.ItemEnhancementPolicy.MaxUpgradeLevel"/>).</summary>
    public int MaxLevel { get; set; }

    /// <summary>Gets or sets the lowest current level from which a failed attempt may drop a level (below it is safe).</summary>
    public int DowngradeMinLevel { get; set; }

    /// <summary>Gets or sets the lowest current level from which a failed attempt may destroy the item.</summary>
    public int DestroyMinLevel { get; set; }

    /// <summary>Gets or sets the gold fee multiplier per item rarity, in percent.</summary>
    public EnhancementRarityPercents GoldRarityPercent { get; set; } = new();

    /// <summary>Gets or sets when an attempt is announced to the whole world.</summary>
    public EnhancementAnnounceRules Announce { get; set; } = new();

    /// <summary>Gets or sets one row per current level <c>0..maxLevel-1</c>.</summary>
    public IReadOnlyList<EnhancementLevelDefinition> Levels { get; set; } = [];

    /// <summary>Gets or sets the protection scrolls.</summary>
    public IReadOnlyList<EnhancementProtectionDefinition> Protections { get; set; } = [];
}

/// <summary>One <c>levels[]</c> row: the attempt from <see cref="From"/> to <c>From + 1</c>.</summary>
public sealed record EnhancementLevelDefinition
{
    /// <summary>Gets or sets the current level this row applies to.</summary>
    public int From { get; set; }

    /// <summary>Gets or sets the success weight, per mille.</summary>
    public int Success { get; set; }

    /// <summary>Gets or sets the no-change weight, per mille.</summary>
    public int NoChange { get; set; }

    /// <summary>Gets or sets the downgrade weight, per mille.</summary>
    public int Downgrade { get; set; }

    /// <summary>Gets or sets the destroy weight, per mille.</summary>
    public int Destroy { get; set; }

    /// <summary>Gets or sets the base gold fee (scaled by rarity).</summary>
    public long Gold { get; set; }

    /// <summary>Gets or sets the materials spent on every attempt.</summary>
    public IReadOnlyList<RecipeItemAmount> Materials { get; set; } = [];
}

/// <summary>One <c>protections[]</c> entry: a scroll spent on an attempt to soften a failure.</summary>
public sealed record EnhancementProtectionDefinition
{
    /// <summary>Gets or sets the item code of the scroll.</summary>
    public string Item { get; set; } = string.Empty;

    /// <summary>Gets or sets whether a downgrade becomes no change.</summary>
    public bool PreventsDowngrade { get; set; }

    /// <summary>Gets or sets whether destruction is prevented (it becomes a downgrade, or no change when downgrades are prevented too).</summary>
    public bool PreventsDestroy { get; set; }
}

/// <summary>Gold fee multiplier per rarity, in percent.</summary>
public sealed record EnhancementRarityPercents
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

/// <summary>When an attempt is broadcast to the whole world.</summary>
public sealed record EnhancementAnnounceRules
{
    /// <summary>Gets or sets the lowest level reached by a success that is announced (<c>0</c> disables).</summary>
    public int SuccessMinLevel { get; set; }

    /// <summary>Gets or sets whether a destroyed item is announced.</summary>
    public bool Destroyed { get; set; }
}
