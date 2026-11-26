// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Economy.Config;

/// <summary>
/// What an engaged player at a level range wears and crafts (<c>economy.json → playerModel.stages</c>).
/// </summary>
public sealed record PlayerStage
{
    /// <summary>Gets the first level of the stage.</summary>
    public int MinLevel { get; set; }

    /// <summary>Gets the last level of the stage.</summary>
    public int MaxLevel { get; set; }

    /// <summary>Gets the item codes of the equipped gear that wears out and is repaired.</summary>
    public IReadOnlyList<string> Gear { get; set; } = [];

    /// <summary>Gets the recipe codes crafted at stations, in rotation (each pays its station fee).</summary>
    public IReadOnlyList<string> Recipes { get; set; } = [];
}
