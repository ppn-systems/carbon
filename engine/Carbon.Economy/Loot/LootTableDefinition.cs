// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Economy.Loot;

/// <summary>
/// JSON shape of one loot table in <c>loot.json</c>, keyed by source id (monster id such as <c>slime</c>).
/// </summary>
public sealed record LootTableDefinition
{
    /// <summary>Gets the source id (monster id from <c>monsters.json</c> or gather node id).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets the item theme tag every dropped item must carry (or <c>generic</c>); defaults to <see cref="Id"/>.</summary>
    public string? Theme { get; set; }

    /// <summary>Gets a value indicating whether this table belongs to an elite variant (rolled with elite multipliers).</summary>
    public bool Elite { get; set; }

    /// <summary>Gets the source kind.</summary>
    public LootSourceKind Kind { get; set; }

    /// <summary>Gets the lowest level this source is designed to spawn at (balance tests sample this range).</summary>
    public int MinLevel { get; set; } = 1;

    /// <summary>Gets the highest level this source is designed to spawn at.</summary>
    public int MaxLevel { get; set; } = 1;

    /// <summary>Gets the gold base multiplied by the level curve (<c>0</c> for no gold).</summary>
    public int GoldBase { get; set; }

    /// <summary>Gets the diamond chance at level 0 in parts-per-million (<c>0</c> for never).</summary>
    public int DiamondBasePpm { get; set; }

    /// <summary>Gets the number of weighted rolls for a normal kill.</summary>
    public int Rolls { get; set; } = 1;

    /// <summary>Gets the extra weighted rolls for elites.</summary>
    public int EliteExtraRolls { get; set; } = 1;

    /// <summary>Gets the extra weighted rolls for bosses.</summary>
    public int BossExtraRolls { get; set; } = 3;

    /// <summary>Gets the weight of rolling nothing.</summary>
    public int NothingWeight { get; set; }

    /// <summary>Gets entries that always drop when their level band allows.</summary>
    public IReadOnlyList<LootEntryDefinition> Guaranteed { get; set; } = [];

    /// <summary>Gets the weighted entries.</summary>
    public IReadOnlyList<LootEntryDefinition> Entries { get; set; } = [];
}
