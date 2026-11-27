// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Items.Progression;

/// <summary>Attribute point budget: per-level brackets plus a bonus on every milestone level.</summary>
public sealed class StatPointsConfig
{
    /// <summary>Gets the brackets, contiguous from level 2 to the max level.</summary>
    public IReadOnlyList<StatPointBracket> Brackets { get; init; } = [];

    /// <summary>Gets the milestone period: every level divisible by it grants <see cref="MilestoneBonus"/> (0 = none).</summary>
    public int MilestoneEvery { get; init; }

    /// <summary>Gets the extra points granted on a milestone level.</summary>
    public int MilestoneBonus { get; init; }
}
