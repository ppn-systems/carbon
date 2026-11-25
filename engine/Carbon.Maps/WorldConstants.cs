// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps;

/// <summary>
/// Terrain thresholds shared by every generation stage, the navigation grid and the landmark rules.
/// Change them here only: each value feeds several systems that must agree, and any change alters
/// generated worlds (bump <c>ProceduralWorldGenerator.GeneratorVersion</c>).
/// </summary>
public static class WorldConstants
{
    /// <summary>Elevation below which land is sea; rivers end here.</summary>
    public const float SeaLevel = 0.32f;

    /// <summary>Elevation below which ground is coast (beach sand, coastal height rules).</summary>
    public const float CoastLevel = 0.38f;

    /// <summary>Width-scaled river distance at or below which a tile is river or lake water.</summary>
    public const float RiverChannel = 2.2f;

    /// <summary>Width-scaled river distance below which a tile counts as near water (no ores or trees).</summary>
    public const float NearWater = 3.8f;

    /// <summary>Width-scaled distance over which rivers lower and moisten the land around them.</summary>
    public const float RiparianReach = 10f;
}
