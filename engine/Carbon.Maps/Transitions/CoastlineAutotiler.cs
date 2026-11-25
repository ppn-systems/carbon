// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;

namespace Carbon.Maps.Transitions;

/// <summary>
/// High-performance procedural autotiler evaluating 8-way neighbor adjacency masks
/// for smooth Marching Squares corner transitions, diagonal beveling, and coastal foam shorelines.
/// Fully zero-allocation on hot rendering loops.
/// </summary>
public static class CoastlineAutotiler
{
    /// <summary>
    /// Evaluates the 8-directional neighbor adjacency mask for a specific target biome.
    /// Uses high-speed local chunk array indexing when within internal chunk bounds [1..30],
    /// with zero-allocation fallback to world queries at chunk boundaries.
    /// </summary>
    /// <param name="chunk">Current resident chunk.</param>
    /// <param name="lx">Local tile X coordinate within the chunk [0..31].</param>
    /// <param name="ly">Local tile Y coordinate within the chunk [0..31].</param>
    /// <param name="wx">Continuous world tile X coordinate.</param>
    /// <param name="wy">Continuous world tile Y coordinate.</param>
    /// <param name="world">Active chunk world streaming coordinator.</param>
    /// <param name="targetBiome">The biome type to check adjacency against.</param>
    /// <returns>Computed 8-bit <see cref="NeighborMask"/>.</returns>
    public static NeighborMask GetAdjacencyMask(
        Chunk2D chunk,
        int lx,
        int ly,
        int wx,
        int wy,
        ChunkWorld2D world,
        BiomeType targetBiome)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(world);

        NeighborMask mask = NeighborMask.None;

        if (GetNeighborBiome(chunk, lx, ly - 1, wx, wy - 1, world) == targetBiome)
        {
            mask |= NeighborMask.North;
        }

        if (GetNeighborBiome(chunk, lx + 1, ly, wx + 1, wy, world) == targetBiome)
        {
            mask |= NeighborMask.East;
        }

        if (GetNeighborBiome(chunk, lx, ly + 1, wx, wy + 1, world) == targetBiome)
        {
            mask |= NeighborMask.South;
        }

        if (GetNeighborBiome(chunk, lx - 1, ly, wx - 1, wy, world) == targetBiome)
        {
            mask |= NeighborMask.West;
        }

        if (GetNeighborBiome(chunk, lx + 1, ly - 1, wx + 1, wy - 1, world) == targetBiome)
        {
            mask |= NeighborMask.NorthEast;
        }

        if (GetNeighborBiome(chunk, lx + 1, ly + 1, wx + 1, wy + 1, world) == targetBiome)
        {
            mask |= NeighborMask.SouthEast;
        }

        if (GetNeighborBiome(chunk, lx - 1, ly + 1, wx - 1, wy + 1, world) == targetBiome)
        {
            mask |= NeighborMask.SouthWest;
        }

        if (GetNeighborBiome(chunk, lx - 1, ly - 1, wx - 1, wy - 1, world) == targetBiome)
        {
            mask |= NeighborMask.NorthWest;
        }

        return mask;
    }

    /// <summary>
    /// Evaluates whether adjacent neighbors are solid land biomes (i.e. not ocean and not water).
    /// Used by water tiles to detect coastal shorelines and riverbanks.
    /// </summary>
    public static NeighborMask GetLandAdjacencyMask(
        Chunk2D chunk,
        int lx,
        int ly,
        int wx,
        int wy,
        ChunkWorld2D world)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(world);

        NeighborMask mask = NeighborMask.None;

        if (IsLand(GetNeighborBiome(chunk, lx, ly - 1, wx, wy - 1, world)))
        {
            mask |= NeighborMask.North;
        }

        if (IsLand(GetNeighborBiome(chunk, lx + 1, ly, wx + 1, wy, world)))
        {
            mask |= NeighborMask.East;
        }

        if (IsLand(GetNeighborBiome(chunk, lx, ly + 1, wx, wy + 1, world)))
        {
            mask |= NeighborMask.South;
        }

        if (IsLand(GetNeighborBiome(chunk, lx - 1, ly, wx - 1, wy, world)))
        {
            mask |= NeighborMask.West;
        }

        if (IsLand(GetNeighborBiome(chunk, lx + 1, ly - 1, wx + 1, wy - 1, world)))
        {
            mask |= NeighborMask.NorthEast;
        }

        if (IsLand(GetNeighborBiome(chunk, lx + 1, ly + 1, wx + 1, wy + 1, world)))
        {
            mask |= NeighborMask.SouthEast;
        }

        if (IsLand(GetNeighborBiome(chunk, lx - 1, ly + 1, wx - 1, wy + 1, world)))
        {
            mask |= NeighborMask.SouthWest;
        }

        if (IsLand(GetNeighborBiome(chunk, lx - 1, ly - 1, wx - 1, wy - 1, world)))
        {
            mask |= NeighborMask.NorthWest;
        }

        return mask;
    }

    /// <summary>
    /// Evaluates whether adjacent neighbors are aquatic biomes (DeepOcean or ShallowWater).
    /// Used by land biomes (e.g. beaches, cliffs) to detect water shorelines for diagonal smoothing.
    /// </summary>
    public static NeighborMask GetWaterAdjacencyMask(
        Chunk2D chunk,
        int lx,
        int ly,
        int wx,
        int wy,
        ChunkWorld2D world)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(world);

        NeighborMask mask = NeighborMask.None;

        if (IsWater(GetNeighborBiome(chunk, lx, ly - 1, wx, wy - 1, world)))
        {
            mask |= NeighborMask.North;
        }

        if (IsWater(GetNeighborBiome(chunk, lx + 1, ly, wx + 1, wy, world)))
        {
            mask |= NeighborMask.East;
        }

        if (IsWater(GetNeighborBiome(chunk, lx, ly + 1, wx, wy + 1, world)))
        {
            mask |= NeighborMask.South;
        }

        if (IsWater(GetNeighborBiome(chunk, lx - 1, ly, wx - 1, wy, world)))
        {
            mask |= NeighborMask.West;
        }

        if (IsWater(GetNeighborBiome(chunk, lx + 1, ly - 1, wx + 1, wy - 1, world)))
        {
            mask |= NeighborMask.NorthEast;
        }

        if (IsWater(GetNeighborBiome(chunk, lx + 1, ly + 1, wx + 1, wy + 1, world)))
        {
            mask |= NeighborMask.SouthEast;
        }

        if (IsWater(GetNeighborBiome(chunk, lx - 1, ly + 1, wx - 1, wy + 1, world)))
        {
            mask |= NeighborMask.SouthWest;
        }

        if (IsWater(GetNeighborBiome(chunk, lx - 1, ly - 1, wx - 1, wy - 1, world)))
        {
            mask |= NeighborMask.NorthWest;
        }

        return mask;
    }

    private static BiomeType GetNeighborBiome(
        Chunk2D chunk,
        int localX,
        int localY,
        int worldX,
        int worldY,
        ChunkWorld2D world)
    {
        if ((uint)localX < Chunk2D.Size && (uint)localY < Chunk2D.Size)
        {
            return chunk.GetTile(localX, localY).Biome;
        }

        return world.GetTileAtWorld(worldX, worldY).Biome;
    }

    private static bool IsLand(BiomeType biome) =>
        biome is not BiomeType.DeepOcean and not BiomeType.ShallowWater;

    private static bool IsWater(BiomeType biome) =>
        biome is BiomeType.DeepOcean or BiomeType.ShallowWater;
}
