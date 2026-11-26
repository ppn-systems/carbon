// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>
/// Gold faucet tunables (<c>economy.json → gold</c>). Mean gold per kill = <c>table.goldBase × level^LevelExponent</c>.
/// </summary>
public sealed record GoldRules
{
    /// <summary>Gets the chance (percent) that a normal monster drops gold at all. Elites and bosses always drop gold.</summary>
    public int DropChancePercent { get; set; } = 70;

    /// <summary>Gets the exponent of the level curve (sub-linear keeps late-game inflation in check).</summary>
    public double LevelExponent { get; set; } = 0.6;

    /// <summary>Gets the uniform ± variance around the mean, in percent.</summary>
    public int VariancePercent { get; set; } = 25;

    /// <summary>Gets the gold multiplier for elite monsters.</summary>
    public int EliteMultiplier { get; set; } = 3;

    /// <summary>Gets the gold multiplier for bosses.</summary>
    public int BossMultiplier { get; set; } = 12;

    /// <summary>Gets the bonus percent per distance tier from the world origin.</summary>
    public int DistanceBonusPercentPerTier { get; set; } = 10;

    /// <summary>Gets the highest distance tier that still adds bonus.</summary>
    public int MaxDistanceTier { get; set; } = 10;

    /// <summary>Gets how many levels below the player a monster may be before it counts as trivial ("gray").</summary>
    public int LowLevelGap { get; set; } = 8;

    /// <summary>Gets the percent of gold a trivial monster still yields.</summary>
    public int LowLevelMultiplierPercent { get; set; } = 20;
}
