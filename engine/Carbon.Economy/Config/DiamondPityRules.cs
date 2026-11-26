// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Economy.Config;

/// <summary>
/// Diamond bad-luck protection (<c>economy.json → diamond.pity</c>). After <see cref="SoftStartKills"/> eligible kills
/// without a diamond, the chance grows by <see cref="StepPpm"/> per kill; at <see cref="HardCapKills"/> a diamond is guaranteed.
/// </summary>
public sealed record DiamondPityRules
{
    /// <summary>Gets a value indicating whether pity is active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets the dry-streak length at which the chance starts ramping.</summary>
    public int SoftStartKills { get; set; } = 10000;

    /// <summary>Gets the parts-per-million added per kill beyond the soft start.</summary>
    public int StepPpm { get; set; } = 1;

    /// <summary>Gets the dry-streak length at which a diamond is guaranteed.</summary>
    public int HardCapKills { get; set; } = 30000;
}
