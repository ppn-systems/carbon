// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Hydrology;

/// <summary>
/// How deep a water tile is, from <see cref="WaterDepth"/>. Tiers only ever grow away from the shore, and a
/// river's tier is capped by its width (ADR-032).
/// </summary>
public enum WaterDepthTier : byte
{
    /// <summary>Dry land.</summary>
    Dry = 0,

    /// <summary>The waterline: ankle-deep, a light wade.</summary>
    Shore = 1,

    /// <summary>Waist-deep water: a slow wade.</summary>
    Shallow = 2,

    /// <summary>Out of your depth: swim on the surface.</summary>
    Medium = 3,

    /// <summary>Deep water: dive.</summary>
    Deep = 4,

    /// <summary>The ocean abyss: dive into the dark.</summary>
    Abyss = 5,
}
