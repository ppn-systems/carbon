// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class PropDomainTests
{
    [Fact]
    public void PropDestructibility_EncapsulatesDurabilityAndDamage()
    {
        PropDestructibility destruct = PropDestructibility.Create(durability: 100f, harvestTier: 1);

        Assert.True(destruct.IsDestructible);
        Assert.False(destruct.IsDestroyed);
        Assert.Equal(100f, destruct.MaxDurability);
        Assert.Equal(100f, destruct.CurrentDurability);
        Assert.Equal(1f, destruct.HealthPercentage);

        // Inflict partial damage
        PropDestructibility damaged = destruct.ApplyDamage(40f);
        Assert.Equal(60f, damaged.CurrentDurability);
        Assert.False(damaged.IsDestroyed);
        Assert.Equal(0.6f, damaged.HealthPercentage, precision: 3);

        // Inflict lethal damage
        PropDestructibility destroyed = damaged.ApplyDamage(70f);
        Assert.Equal(0f, destroyed.CurrentDurability);
        Assert.True(destroyed.IsDestroyed);
        Assert.Equal(0f, destroyed.HealthPercentage);
    }

    [Fact]
    public void MapProp_DifferentiatesTrunkCollisionFromCanopyPassage()
    {
        // Oak Tree located at (100, 100)
        // Trunk collision radius = 8f, Overhead canopy radius = 42f, CanopyOffsetY = -22f
        MapProp oak = MapProp.Create(
            type: MapPropType.OakTree,
            category: PropCategory.Flora,
            destructibility: PropDestructibility.Create(100f, 1),
            localX: 3,
            localY: 3,
            worldX: 100f,
            worldY: 100f,
            radius: 42f,
            collisionRadius: 8f,
            canopyRadius: 42f,
            canopyOffsetY: -22f,
            isSolid: true,
            colorPrimary: 0x166534FF,
            colorSecondary: 0x78350FFF);

        Assert.True(oak.HasCollision);
        Assert.True(oak.HasCanopy);
        Assert.Equal(8f, oak.CollisionRadius);
        Assert.Equal(42f, oak.CanopyRadius);

        // Point directly under the canopy shade: (100, 85) -> distance to CanopyCenter(100, 78) is 7px <= 42px
        Vector2 underCanopy = new(100f, 85f);
        Assert.True(oak.IsUnderCanopy(underCanopy));

        // Point outside the canopy: (100, 200) -> distance is 122px > 42px
        Vector2 outside = new(100f, 200f);
        Assert.False(oak.IsUnderCanopy(outside));
    }

    [Fact]
    public void ChunkWorld2D_ResolveEntityCollision_PermitsCanopyPassageWhileBlockingTrunk()
    {
        ProceduralWorldGenerator generator = new(1337UL);
        ChunkWorld2D world = new(generator, tileSize: 32f);

        // Load chunks around origin
        world.UpdateAroundPosition(Vector2.Zero, chunkRadius: 1, evictionRadius: 3);
        Data.Chunk2D chunk = world.GetOrCreateChunk(0, 0);

        // Place a custom test tree at (200, 200)
        MapProp testTree = MapProp.Create(
            type: MapPropType.OakTree,
            category: PropCategory.Flora,
            destructibility: PropDestructibility.Create(100f, 1),
            localX: 6,
            localY: 6,
            worldX: 200f,
            worldY: 200f,
            radius: 42f,
            collisionRadius: 8f,
            canopyRadius: 42f,
            canopyOffsetY: -22f,
            isSolid: true,
            colorPrimary: 0x166534FF,
            colorSecondary: 0x78350FFF);
        chunk.AddProp(testTree);

        // 1. Entity walking under canopy at (200, 180) -> 20px from trunk (outside collision radius 8 + 7 = 15)
        Vector2 canopyPos = new(200f, 180f);
        bool collidedCanopy = world.ResolveEntityCollision(ref canopyPos, radius: 7f, out _);
        Assert.False(collidedCanopy);
        Assert.Equal(new Vector2(200f, 180f), canopyPos); // Position unchanged, player walks freely!

        // 2. Entity trying to step directly on the trunk at (200, 205) -> 5px from trunk (inside 15px radius)
        Vector2 trunkPos = new(200f, 205f);
        bool collidedTrunk = world.ResolveEntityCollision(ref trunkPos, radius: 7f, out _);
        Assert.True(collidedTrunk);
        // Entity should be pushed out along Y axis to at least 15px distance
        float newDist = Vector2.Distance(trunkPos, new Vector2(200f, 200f));
        Assert.True(newDist >= 14.99f);
    }

    [Fact]
    public void MapProp_ComputesGroundFootprintY_ForDepthSorting()
    {
        // Oak Tree at (100, 100) with scale 1.0f
        MapProp oak = MapProp.Create(
            type: MapPropType.OakTree,
            category: PropCategory.Flora,
            destructibility: PropDestructibility.Create(100f, 1),
            localX: 3,
            localY: 3,
            worldX: 100f,
            worldY: 100f,
            radius: 42f,
            collisionRadius: 8f,
            canopyRadius: 42f,
            canopyOffsetY: -22f,
            isSolid: true,
            colorPrimary: 0x166534FF,
            colorSecondary: 0x78350FFF,
            scale: 1.0f);

        // Ground footprint Y should be worldY + (14f * scale) = 114f
        Assert.Equal(114f, oak.GroundFootprintY);

        // Entity standing north of the tree base (feet at Y = 95f)
        float northPlayerFootY = 95f;
        Assert.True(northPlayerFootY < oak.GroundFootprintY, "North entity must have smaller FootY to be rendered behind trunk");

        // Entity standing south of the tree base (feet at Y = 125f)
        float southPlayerFootY = 125f;
        Assert.True(southPlayerFootY > oak.GroundFootprintY, "South entity must have larger FootY to be rendered in front of trunk");

        // Mineral deposit at (50, 50) with radius 16f
        MapProp ore = MapProp.Create(
            type: MapPropType.CopperVein,
            category: PropCategory.Mineral,
            destructibility: PropDestructibility.Create(200f, 1),
            localX: 1,
            localY: 1,
            worldX: 50f,
            worldY: 50f,
            radius: 16f,
            collisionRadius: 16f,
            canopyRadius: 0f,
            canopyOffsetY: 0f,
            isSolid: true,
            colorPrimary: 0xB45309FF,
            colorSecondary: 0xFDE68AFF,
            scale: 1.0f);

        Assert.Equal(66f, ore.GroundFootprintY);

        // Birch Tree at (200, 200) with scale 1.0f -> footprint at 200 + 22 = 222f
        MapProp birch = MapProp.Create(
            type: MapPropType.BirchTree,
            category: PropCategory.Flora,
            destructibility: PropDestructibility.Create(80f, 1),
            localX: 6,
            localY: 6,
            worldX: 200f,
            worldY: 200f,
            radius: 36f,
            collisionRadius: 8f,
            canopyRadius: 36f,
            canopyOffsetY: -22f,
            isSolid: true,
            colorPrimary: 0x15803DFF,
            colorSecondary: 0xE2E8F0FF,
            scale: 1.0f);

        Assert.Equal(222f, birch.GroundFootprintY);
    }

    [Fact]
    public void GroundYSorting_MaintainsCorrectDepthOrderingBetweenEntitiesAndObstacles()
    {
        // Oak Tree at (100, 100) -> GroundFootprintY = 114f
        MapProp oak = MapProp.Create(
            type: MapPropType.OakTree,
            category: PropCategory.Flora,
            destructibility: PropDestructibility.Create(100f, 1),
            localX: 3,
            localY: 3,
            worldX: 100f,
            worldY: 100f,
            radius: 42f,
            collisionRadius: 8f,
            canopyRadius: 42f,
            canopyOffsetY: -22f,
            isSolid: true,
            colorPrimary: 0x166534FF,
            colorSecondary: 0x78350FFF,
            scale: 1.0f);

        // Simulation items list
        List<(float SortY, string Id)> renderList = new(4);

        // Case 1: Player stands BEHIND tree (North, center Y = 80, foot Y = 90)
        float playerBehindFootY = 90f;
        renderList.Clear();
        renderList.Add((oak.GroundFootprintY, "TreeTrunk"));
        renderList.Add((playerBehindFootY, "PlayerAvatar"));
        renderList.Sort((a, b) => a.SortY.CompareTo(b.SortY));

        // When player is behind, Player must be rendered first, Tree trunk on top
        Assert.Equal("PlayerAvatar", renderList[0].Id);
        Assert.Equal("TreeTrunk", renderList[1].Id);

        // Case 2: Player moves in FRONT of tree (South, center Y = 120, foot Y = 130)
        float playerInFrontFootY = 130f;
        renderList.Clear();
        renderList.Add((oak.GroundFootprintY, "TreeTrunk"));
        renderList.Add((playerInFrontFootY, "PlayerAvatar"));
        renderList.Sort((a, b) => a.SortY.CompareTo(b.SortY));

        // When player is in front, Tree trunk rendered first, Player rendered on top
        Assert.Equal("TreeTrunk", renderList[0].Id);
        Assert.Equal("PlayerAvatar", renderList[1].Id);
    }

    [Fact]
    public void BiomePropResolver_FoliageDensity_EnforcesMeadowClearingsAndDenseCopses()
    {
        ulong seed = 1337UL;

        // In LushPlains, low foliage density (< 0.48f) must guarantee open meadows (0 trees)
        int clearingTreeCount = 0;
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                if (BiomePropResolver.TryResolveProp(
                    x, y, x, y,
                    Biomes.BiomeType.LushPlains,
                    elevation: 0.40f,
                    foliageDensity: 0.25f,
                    mineralNoise: 0.10f,
                    isNearWater: false,
                    tileSize: 32f,
                    seed,
                    out MapProp prop) && prop.IsSolid)
                {
                    clearingTreeCount++;
                }
            }
        }

        Assert.Equal(0, clearingTreeCount);

        // High foliage density (>= 0.72f) must produce trees in valid slots
        int copseTreeCount = 0;
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                if (BiomePropResolver.TryResolveProp(
                    x, y, x, y,
                    Biomes.BiomeType.LushPlains,
                    elevation: 0.40f,
                    foliageDensity: 0.85f,
                    mineralNoise: 0.10f,
                    isNearWater: false,
                    tileSize: 32f,
                    seed,
                    out MapProp prop) && prop.IsSolid)
                {
                    copseTreeCount++;
                }
            }
        }

        Assert.True(copseTreeCount > 0, "High foliage density copse must contain trees.");
    }

    [Fact]
    public void BiomePropResolver_MineralVein_EnforcesCleanGroundAndClustersInActiveVeins()
    {
        ulong seed = 9999UL;

        // Normal ground with low mineral noise (< 0.68f) must have ZERO mineral ores
        int barrenOreCount = 0;
        for (int y = 0; y < 30; y++)
        {
            for (int x = 0; x < 30; x++)
            {
                if (BiomePropResolver.TryResolveProp(
                    x, y, x % 32, y % 32,
                    Biomes.BiomeType.RedMesa,
                    elevation: 0.60f,
                    foliageDensity: 0.20f,
                    mineralNoise: 0.30f,
                    isNearWater: false,
                    tileSize: 32f,
                    seed,
                    out MapProp prop) && prop.Type is MapPropType.CopperVein or MapPropType.IronVein or MapPropType.GoldVein)
                {
                    barrenOreCount++;
                }
            }
        }

        Assert.Equal(0, barrenOreCount);

        // Active geological vein (mineral noise >= 0.70f in highlands) must spawn concentrated ore nodes
        int veinOreCount = 0;
        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                if (BiomePropResolver.TryResolveProp(
                    x, y, x, y,
                    Biomes.BiomeType.RedMesa,
                    elevation: 0.60f,
                    foliageDensity: 0.20f,
                    mineralNoise: 0.85f,
                    isNearWater: false,
                    tileSize: 32f,
                    seed,
                    out MapProp prop) && prop.Type is MapPropType.CopperVein or MapPropType.IronVein or MapPropType.GoldVein)
                {
                    veinOreCount++;
                }
            }
        }

        Assert.True(veinOreCount >= 2, $"Active geological vein must spawn clustered ore nodes (found {veinOreCount}).");
    }

    [Fact]
    public void BiomePropResolver_WaterClearance_KeepsRiverbanksClearOfSolidObstacles()
    {
        ulong seed = 42UL;

        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++)
            {
                if (BiomePropResolver.TryResolveProp(
                    x, y, x, y,
                    Biomes.BiomeType.AncientForest,
                    elevation: 0.40f,
                    foliageDensity: 0.90f,
                    mineralNoise: 0.90f,
                    isNearWater: true,
                    tileSize: 32f,
                    seed,
                    out MapProp prop))
                {
                    Assert.False(prop.IsSolid, "Riverbank tiles must remain free of solid tree trunks and boulders.");
                }
            }
        }
    }
}
