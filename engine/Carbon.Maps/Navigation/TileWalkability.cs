// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Navigation;

/// <summary>
/// Domain classification of a map tile's traversal capability for player movement and monster pathfinding.
/// </summary>
public enum TileWalkability : byte
{
    /// <summary>
    /// Impassable barrier: Deep ocean, cliff wall without ramp, solid tree trunk, mineral rock, solid wall, closed door.
    /// </summary>
    Impassable = 0,

    /// <summary>
    /// Standard walkable terrain: Grass, dirt, sand, open plains (Cost multiplier = 1.0f).
    /// </summary>
    WalkableNormal = 1,

    /// <summary>
    /// Slow movement terrain: Shallow water, murky swamp mud (Cost multiplier = 1.4f - 2.0f).
    /// </summary>
    WalkableSlow = 2,

    /// <summary>
    /// Accelerated movement terrain: Constructed wood floor, stone pavers, cobble paths (Cost multiplier = 0.8f - 0.85f).
    /// </summary>
    WalkableFast = 3,

    /// <summary>
    /// Sloped incline: Walkable ramp or stairs connecting different height levels (Cost multiplier = 1.2f).
    /// </summary>
    WalkableRamp = 4
}
