// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Navigation;
using Carbon.Maps.Noise;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Maps.Topography;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class MmorpgWorldGenV12Tests
{
    private static readonly PointOfInterestType[] s_expectedNewPois =
    [
        PointOfInterestType.AncientRuins,
        PointOfInterestType.BanditCamp,
        PointOfInterestType.Watchtower,
        PointOfInterestType.BossArena,
    ];

    [Fact]
    public void SimplexNoise2D_NegativeIntegerCoordinates_AreContinuous()
    {
        SimplexNoise2D noise = new(42UL);

        float a0 = noise.Evaluate(-1.001f, -2.001f);
        float a1 = noise.Evaluate(-1.0f, -2.0f);
        float a2 = noise.Evaluate(-0.999f, -1.999f);
        Assert.InRange(MathF.Abs(a1 - a0), 0f, 0.02f);
        Assert.InRange(MathF.Abs(a2 - a1), 0f, 0.02f);

        float b0 = noise.Evaluate(-16.001f, 3.5f);
        float b1 = noise.Evaluate(-16.0f, 3.5f);
        float b2 = noise.Evaluate(-15.999f, 3.5f);
        Assert.InRange(MathF.Abs(b1 - b0), 0f, 0.02f);
        Assert.InRange(MathF.Abs(b2 - b1), 0f, 0.02f);
    }

    [Fact]
    public void BiomePropResolver_DenseForest_UsesSqrt5LatticeWithoutGlobalRampStripping()
    {
        List<MapProp> spawned = [];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                if (BiomePropResolver.TryResolveProp(
                    x,
                    y,
                    x,
                    y,
                    BiomeType.AncientForest,
                    elevation: 0.52f,
                    foliageDensity: 0.88f,
                    mineralNoise: 0.20f,
                    isNearWater: false,
                    tileSize: 32f,
                    seed: 42UL,
                    out MapProp prop))
                {
                    spawned.Add(prop);
                }
            }
        }

        Assert.True(spawned.Count >= 100, $"Expected dense ancient forest to populate freely, actual {spawned.Count}.");

        List<MapProp> trees = [];
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i].Category == PropCategory.Flora && spawned[i].HasCanopy)
            {
                trees.Add(spawned[i]);
            }
        }

        for (int i = 0; i < trees.Count; i++)
        {
            for (int j = i + 1; j < trees.Count; j++)
            {
                int dx = trees[i].LocalTileX - trees[j].LocalTileX;
                int dy = trees[i].LocalTileY - trees[j].LocalTileY;
                float tileDist = MathF.Sqrt((dx * dx) + (dy * dy));
                Assert.True(tileDist >= 2.0f, $"Trees too close at ({trees[i].LocalTileX},{trees[i].LocalTileY}) and ({trees[j].LocalTileX},{trees[j].LocalTileY}).");
            }
        }
    }

    [Fact]
    public void Props_EnforceHarvestTiers_IndestructibleShrines_AndTickRespawn()
    {
        MapProp shrine = MapProp.Create(MapPropType.AncientShrine, 1, 1, 48f, 48f, 24f, isSolid: true, 0x818CF8FFu, 0x4338CAFFu, 1.2f);
        Assert.False(shrine.Destructibility.IsDestructible);

        MapProp gold = MapProp.Create(MapPropType.GoldVein, 2, 2, 80f, 80f, 20f, isSolid: true, 0xFFD700FFu, 0xB8860BFFu, 1f);
        Assert.Equal((byte)3, gold.Destructibility.HarvestTier);

        MapProp bush = MapProp.Create(MapPropType.Bush, 3, 3, 112f, 112f, 14f, isSolid: false, 0x22C55EFFu, 0x15803DFFu, 1f);
        Assert.Equal((byte)0, bush.Destructibility.HarvestTier);

        ChunkWorld2D world = new(new ProceduralWorldGenerator(42UL), tileSize: 32f);
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);

        int targetWorldX = 0;
        int targetWorldY = 0;
        int targetLocalX = 0;
        int targetLocalY = 0;
        int targetChunkX = 0;
        int targetChunkY = 0;
        MapProp found = default;
        bool hasProp = false;

        for (int cy = -1; cy <= 1 && !hasProp; cy++)
        {
            for (int cx = -1; cx <= 1 && !hasProp; cx++)
            {
                Chunk2D chunk = world.GetOrCreateChunk(cx, cy);
                for (int i = 0; i < chunk.Props.Count; i++)
                {
                    MapProp candidate = chunk.Props[i];
                    if (candidate.Destructibility.IsDestructible && candidate.Destructibility.HarvestTier >= 1)
                    {
                        found = candidate;
                        targetChunkX = cx;
                        targetChunkY = cy;
                        targetLocalX = candidate.LocalTileX;
                        targetLocalY = candidate.LocalTileY;
                        targetWorldX = (cx * Chunk2D.Size) + candidate.LocalTileX;
                        targetWorldY = (cy * Chunk2D.Size) + candidate.LocalTileY;
                        hasProp = true;
                        break;
                    }
                }
            }
        }

        Assert.True(hasProp, "Expected at least one harvestable natural prop near origin.");

        byte weakTier = (byte)(found.Destructibility.HarvestTier - 1);
        Assert.False(world.TryDamagePropAt(targetWorldX, targetWorldY, 500f, weakTier, out MapProp blocked));
        Assert.False(blocked.Destructibility.IsDestroyed);

        Assert.True(world.TryDamagePropAt(targetWorldX, targetWorldY, 10_000f, found.Destructibility.HarvestTier, respawnAtTick: 120UL, out MapProp destroyed));
        Assert.True(destroyed.Destructibility.IsDestroyed);

        Assert.Equal(0, world.ProcessPropRespawns(119UL));
        Assert.Equal(1, world.ProcessPropRespawns(120UL));

        Chunk2D rebuilt = world.GetOrCreateChunk(targetChunkX, targetChunkY);
        int idx = rebuilt.FindPropIndexAt(targetLocalX, targetLocalY);
        Assert.True(idx >= 0);
        Assert.False(rebuilt.Props[idx].Destructibility.IsDestroyed);
    }

    [Fact]
    public void CliffAutotiler_CanStepAcross_IsDirectional()
    {
        static byte GetHeight(int x, int y) => x == 1 && y <= 1 ? (byte)1 : (byte)0;
        static RampDirection GetRamp(int x, int y) => x == 1 && y == 1 ? RampDirection.South : RampDirection.None;

        Assert.True(CliffAutotiler.CanStepAcross(1, 2, 1, 1, GetHeight, GetRamp));
        Assert.False(CliffAutotiler.CanStepAcross(0, 1, 1, 1, GetHeight, GetRamp));
    }

    [Fact]
    public void RiverNetwork_RegionSeam512_IsContinuous()
    {
        // The river field is stamped out to 12.5 width-scaled tiles (ADR-032); beyond that a tile reads as far away.
        const float maxTrackableDistance = 12.5f;
        ProceduralWorldGenerator generator = new(42UL);
        for (int coord = -256; coord <= 256; coord += 4)
        {
            float dLeft = MathF.Min(generator.GetRiverDistance(511, coord), maxTrackableDistance);
            float dRight = MathF.Min(generator.GetRiverDistance(512, coord), maxTrackableDistance);
            Assert.True(MathF.Abs(dRight - dLeft) <= 2.5f, $"Horizontal seam jump at (511..512, {coord}): {dLeft} -> {dRight}");

            float dTop = MathF.Min(generator.GetRiverDistance(coord, 511), maxTrackableDistance);
            float dBottom = MathF.Min(generator.GetRiverDistance(coord, 512), maxTrackableDistance);
            Assert.True(MathF.Abs(dBottom - dTop) <= 2.5f, $"Vertical seam jump at ({coord}, 511..512): {dTop} -> {dBottom}");
        }
    }

    [Fact]
    public void Bridges_AllowFastWalkability_SpeedBonus_AndDryCollisionOverWater()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(99UL), tileSize: 32f);
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);

        Chunk2D chunk = world.GetOrCreateChunk(0, 0);
        _ = chunk.RemoveProps(static p => p.LocalTileX == 10 && p.LocalTileY == 10);
        chunk.SetTile(10, 10, new MapTile(BiomeType.DeepOcean, 0.10f, 0.5f, 0.5f, true, 0));
        chunk.SetBridge(10, 10);

        Assert.True(world.IsBridgeAtWorld(10, 10));
        Assert.Equal(TileWalkability.WalkableFast, world.NavGrid.GetWalkability(10, 10));

        Vector2 bridgeCenter = new(10.5f * 32f, 10.5f * 32f);
        Assert.Equal(1.20f, world.GetMoveSpeedMultiplier(bridgeCenter));
        Assert.False(world.ResolveEntityCollision(ref bridgeCenter, 8f, out _));
    }

    [Fact]
    public void WorldProgression_ScalesStarterToEndgameAndPlansNewLandmarks()
    {
        Assert.True(WorldProgression.GetStarterWeight(42UL, 0, 0) > 0.90f);
        Assert.Equal((byte)0, WorldProgression.GetZoneTier(42UL, 0, 0));
        Assert.Equal((byte)1, WorldProgression.GetZoneTier(42UL, 1000, 0));
        Assert.Equal((byte)2, WorldProgression.GetZoneTier(42UL, 2000, 0));
        Assert.Equal((byte)3, WorldProgression.GetZoneTier(42UL, 3600, 3600));

        for (int i = 0; i < s_expectedNewPois.Length; i++)
        {
            Assert.NotNull(LandmarkPlanners.Get(s_expectedNewPois[i]));
        }

        ProceduralWorldGenerator generator = new(42UL);
        bool sawNewPoiWithPieces = false;
        for (int sy = -10; sy <= 10 && !sawNewPoiWithPieces; sy++)
        {
            for (int sx = -10; sx <= 10; sx++)
            {
                PointOfInterest poi = generator.Landmarks.GetSite(sx, sy);
                if (Array.IndexOf(s_expectedNewPois, poi.Type) >= 0)
                {
                    LandmarkLayout layout = generator.Landmarks.GetLayout(sx, sy);
                    Assert.True(layout.Pieces.Length > 0);
                    sawNewPoiWithPieces = true;
                    break;
                }
            }
        }

        Assert.True(sawNewPoiWithPieces, "Expected at least one new MMORPG POI in [-10..10] sites.");
    }
}
