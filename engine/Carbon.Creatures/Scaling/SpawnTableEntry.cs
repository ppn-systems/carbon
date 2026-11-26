// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Creatures.Scaling;

/// <summary>
/// Weighted entry of a biome spawn table.
/// </summary>
public sealed class SpawnTableEntry
{
    /// <summary>Gets the monster identifier.</summary>
    public string Monster { get; set; } = string.Empty;

    /// <summary>Gets the daytime weight (0 disables daytime spawns).</summary>
    public float Weight { get; set; }

    /// <summary>Gets the night weight; defaults to <see cref="Weight"/> when omitted.</summary>
    public float? NightWeight { get; set; }
}
