// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Economy.Config;

/// <summary>
/// What an engaged player at a level range wields, crafts and consumes (<c>economy.json → playerModel.stages</c>).
/// </summary>
public sealed record PlayerStage
{
    /// <summary>Gets the first level of the stage.</summary>
    public int MinLevel { get; set; }

    /// <summary>Gets the last level of the stage.</summary>
    public int MaxLevel { get; set; }

    /// <summary>
    /// Gets the item code of the wielded weapon. Combat wears only the active weapon (armor never loses durability), at the
    /// rate <c>ToolUsagePolicy.EvaluateCombatStrike</c> applies per strike.
    /// </summary>
    public string Weapon { get; set; } = string.Empty;

    /// <summary>Gets the weapon strikes needed per kill at this stage (monster HP over the stage's weapon damage).</summary>
    public int StrikesPerKill { get; set; } = 1;

    /// <summary>Gets the recipe codes crafted at stations, in rotation (each pays its station fee).</summary>
    public IReadOnlyList<string> Recipes { get; set; } = [];

    /// <summary>Gets the consumables (food, potions) bought from vendors every hour.</summary>
    public IReadOnlyList<ConsumableUse> Consumables { get; set; } = [];
}
