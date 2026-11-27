// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Props;

/// <summary>
/// How far a tree has grown back (ADR-040 D1). A felled tree leaves a stump, which grows back through sapling and young
/// tree to mature. Values are stored and sent on the wire: never renumber them.
/// </summary>
public enum TreeGrowthStage : byte
{
    /// <summary>A full-grown tree, as the world generator places it (and every non-tree prop).</summary>
    Mature = 0,

    /// <summary>What a felled tree leaves: no trunk collider or canopy; dug up, it is gone for good.</summary>
    Stump = 1,

    /// <summary>A sapling growing from the stump (or planted): no collider or canopy.</summary>
    Sapling = 2,

    /// <summary>A young tree: smaller, with a trunk and canopy; felled, it leaves a stump.</summary>
    Young = 3,
}
