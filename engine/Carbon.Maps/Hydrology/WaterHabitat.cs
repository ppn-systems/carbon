// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Physics.Obstacles;

namespace Carbon.Maps.Hydrology;

/// <summary>
/// The depth tiers a water creature lives in (ADR-032 D14), from <paramref name="Shallowest"/> to
/// <paramref name="Deepest"/>. It spawns, moves and paths only inside them; dry land and other tiers are walls.
/// </summary>
/// <param name="Shallowest">Shallowest tier (Shore to Abyss).</param>
/// <param name="Deepest">Deepest tier, at least <paramref name="Shallowest"/>.</param>
public readonly record struct WaterHabitat(WaterDepthTier Shallowest, WaterDepthTier Deepest)
{
    /// <summary>Gets whether the range is a valid, non-empty range of water tiers.</summary>
    public bool IsValid => this.Shallowest >= WaterDepthTier.Shore && this.Deepest <= WaterDepthTier.Abyss && this.Shallowest <= this.Deepest;

    /// <summary>Gets whether the creature lives in water of <paramref name="tier"/>.</summary>
    /// <param name="tier">The depth tier.</param>
    /// <returns><c>true</c> inside the range.</returns>
    public bool Contains(WaterDepthTier tier) => tier >= this.Shallowest && tier <= this.Deepest && tier != WaterDepthTier.Dry;

    /// <summary>Gets <paramref name="mask"/> bound to this habitat (see <see cref="TerrainMask.WaterBound"/>).</summary>
    /// <param name="mask">The collider's collision mask.</param>
    /// <returns>The water-bound mask.</returns>
    public uint ToTerrainMask(uint mask) => TerrainMask.WaterBound(mask, (int)this.Shallowest, (int)this.Deepest);
}
