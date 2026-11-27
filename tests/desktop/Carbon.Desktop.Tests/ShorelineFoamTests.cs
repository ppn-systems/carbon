// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.Atmospherics;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Desktop.Rendering.World;
using Carbon.Graphics.Batching.Procedural;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Shoreline foam (docs/art/graphics-quality.md, water): every water tile that touches land gets a lapping wave that
/// rolls toward that land, out of step with its neighbours.
/// </summary>
public sealed class ShorelineFoamTests
{
    [Fact]
    public void Bake_ShallowWaterByAShore_PointsEveryFoamSpotAtLand()
    {
        CarbonVectorArtLibrary art = CarbonVectorArtLibrary.LoadOrDefault() ?? throw new InvalidOperationException("resources/art is required");
        ProceduralWorldGenerator generator = new(1337UL);
        ChunkWorld2D world = new(generator);
        (int sx, int sy) = generator.SpawnTile;
        BiomeLocator.TryFind(generator, BiomeType.ShallowWater, sx, sy, 2400, out int wx, out int wy).Should().BeTrue();
        Vector2 pos = new((wx + 0.5f) * world.TileSize, (wy + 0.5f) * world.TileSize);
        world.UpdateAroundPosition(pos, chunkRadius: 1, evictionRadius: 400);
        world.TryGetChunk(Chunk2D.ToChunkCoord(wx), Chunk2D.ToChunkCoord(wy), out Chunk2D? chunk).Should().BeTrue();
        ChunkGeometryBaker baker = new();

        baker.Bake(new ChunkMeshBuilder(), chunk!, world, art);

        int shore = 0;
        foreach (ChunkTerrainRenderer.RippleSpot spot in baker.Ripples)
        {
            if (!spot.IsShore)
            {
                continue;
            }

            shore++;
            spot.ToShore.Length().Should().BeApproximately(1f, 1e-4f);
            int tx = (int)MathF.Floor(spot.Centre.X / world.TileSize);
            int ty = (int)MathF.Floor(spot.Centre.Y / world.TileSize);
            TerrainSurface.IsWater(world.GetTileAtWorld(tx, ty).Biome).Should().BeTrue("foam sits on a water tile");

            // At least one side neighbour in the foam's direction is land.
            int sideX = spot.ToShore.X > 0.3f ? 1 : spot.ToShore.X < -0.3f ? -1 : 0;
            int sideY = spot.ToShore.Y > 0.3f ? 1 : spot.ToShore.Y < -0.3f ? -1 : 0;
            bool landX = sideX != 0 && !TerrainSurface.IsWater(world.GetTileAtWorld(tx + sideX, ty).Biome);
            bool landY = sideY != 0 && !TerrainSurface.IsWater(world.GetTileAtWorld(tx, ty + sideY).Biome);
            (landX || landY).Should().BeTrue($"the wave at tile ({tx},{ty}) must roll toward land");
        }

        shore.Should().BeGreaterThan(0, "a wading spot is framed by a shore");
    }

    [Fact]
    public void ShoreWave_OverOneCycle_RollsInThenFades()
    {
        const uint Seed = 77u;
        float previousCrest = -1f;
        bool sawWash = false;
        float start = FindCycleStart(Seed);
        for (int i = 0; i < 120; i++)
        {
            (float crest, float alpha) = AmbientAnimator.ShoreWave(Seed, start + (i * 0.05f));
            if (crest < 1f && sawWash)
            {
                break; // the next wave has started
            }

            if (crest < 1f)
            {
                crest.Should().BeGreaterThanOrEqualTo(previousCrest, "the crest only moves toward the shore while it rolls in");
                previousCrest = crest;
            }
            else
            {
                sawWash = true;
            }

            alpha.Should().BeInRange(0f, 1f);
        }

        previousCrest.Should().BeGreaterThan(0.5f, "the crest reaches most of the way in");
        sawWash.Should().BeTrue("then the wash lingers at the waterline");
    }

    [Fact]
    public void ShoreWave_NeighbouringTiles_AreOutOfStep()
    {
        int differ = 0;
        for (uint seed = 1; seed <= 50; seed++)
        {
            float a = AmbientAnimator.ShoreWave(seed, 10f).Crest;
            float b = AmbientAnimator.ShoreWave(seed + 1, 10f).Crest;
            differ += MathF.Abs(a - b) > 0.1f ? 1 : 0;
        }

        differ.Should().BeGreaterThan(25, "a coast should shimmer, not pulse in unison");
    }

    [Fact]
    public void ShoreWave_EveryFrame_AllocatesNothing()
    {
        _ = AmbientAnimator.ShoreWave(1u, 0f);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            _ = AmbientAnimator.ShoreWave((uint)i, i * 0.016f);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }

    // A time at which the wave of this tile has just started rolling in (crest near 0).
    private static float FindCycleStart(uint seed)
    {
        for (float t = 0f; t < 10f; t += 0.01f)
        {
            if (AmbientAnimator.ShoreWave(seed, t).Crest < 0.05f)
            {
                return t;
            }
        }

        throw new InvalidOperationException("no wave start found");
    }
}
