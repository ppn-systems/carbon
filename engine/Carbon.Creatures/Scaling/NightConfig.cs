// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Creatures.Scaling;

/// <summary>
/// Night empowerment settings. Night is decided from a time-of-day hour (<c>0..24</c>) supplied by the host
/// simulation, never from client state.
/// </summary>
public sealed class NightConfig
{
    /// <summary>Gets the hour night begins (inclusive).</summary>
    public float StartHour { get; set; } = 19f;

    /// <summary>Gets the hour night ends (exclusive).</summary>
    public float EndHour { get; set; } = 5f;

    /// <summary>Gets the multiplier applied to health, damage, and armor of monsters spawned at night.</summary>
    public float StatMultiplier { get; set; } = 1f;

    /// <summary>Gets the multiplier applied to per-cell density caps and spawn rate at night.</summary>
    public float DensityMultiplier { get; set; } = 1f;

    /// <summary>Gets the chance that a night spawn is upgraded to its elite variant.</summary>
    public float EliteChance { get; set; }

    /// <summary>
    /// Determines whether <paramref name="hour"/> falls inside the night window (wrapping past midnight).
    /// </summary>
    /// <param name="hour">Time of day in hours; any value is wrapped into <c>[0, 24)</c>.</param>
    /// <returns><see langword="true"/> at night.</returns>
    public bool IsNight(float hour)
    {
        float h = ((hour % 24f) + 24f) % 24f;
        return this.StartHour <= this.EndHour
            ? h >= this.StartHour && h < this.EndHour
            : h >= this.StartHour || h < this.EndHour;
    }
}
