// Copyright (c) 2026 PPN Systems. All rights reserved.

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
}
