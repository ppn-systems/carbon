// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Creatures.Scaling;

/// <summary>
/// One contiguous distance band of the level curve. Inside a band the level eases from <see cref="StartLevel"/>
/// to <see cref="EndLevel"/> with a smoothstep, so bands read as plateaus joined by smooth ramps.
/// </summary>
public sealed class LevelBand
{
    /// <summary>Gets the band name (e.g. "heartland").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets the inclusive start distance from the world origin, in world units.</summary>
    public float StartDistance { get; set; }

    /// <summary>Gets the exclusive end distance from the world origin, in world units.</summary>
    public float EndDistance { get; set; }

    /// <summary>Gets the level at <see cref="StartDistance"/>.</summary>
    public float StartLevel { get; set; }

    /// <summary>Gets the level at <see cref="EndDistance"/>.</summary>
    public float EndLevel { get; set; }
}
