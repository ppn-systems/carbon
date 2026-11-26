// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>
/// Diamond faucet tunables (<c>economy.json → diamond</c>). Chance per kill in parts-per-million =
/// <c>table.diamondBasePpm × (100 + level × LevelBonusPercentPerLevel) / 100</c>, then elite/boss/distance multipliers.
/// </summary>
public sealed record DiamondRules
{
    /// <summary>Gets the percent added to the base chance per monster level.</summary>
    public int LevelBonusPercentPerLevel { get; set; } = 10;

    /// <summary>Gets the chance multiplier for elite monsters.</summary>
    public int EliteMultiplier { get; set; } = 5;

    /// <summary>Gets the chance multiplier for bosses.</summary>
    public int BossMultiplier { get; set; } = 40;

    /// <summary>Gets the bonus percent per distance tier.</summary>
    public int DistanceBonusPercentPerTier { get; set; } = 25;

    /// <summary>Gets the hard ceiling on the per-kill chance before pity (parts-per-million).</summary>
    public int MaxChancePpm { get; set; } = 50000;

    /// <summary>Gets the bad-luck protection rules.</summary>
    public DiamondPityRules Pity { get; set; } = new();
}
