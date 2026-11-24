// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Navigation;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class LandmarkCollisionTests
{
    private const float TileSize = 32f;

    [Fact]
    public void ResolveEntityCollision_StandingInAWall_IsPushedOut()
    {
        (ChunkWorld2D world, LandmarkPiece house) = FindHouse();

        // Centre of the left wall
        Vector2 inWall = new((house.X - (house.Width * 0.5f) + (LandmarkPiece.WallTiles * 0.5f)) * TileSize, (house.Y - (house.Depth * 0.5f)) * TileSize);
        Vector2 before = inWall;
        bool collided = world.ResolveEntityCollision(ref inWall, radius: 7f, out _);

        Assert.True(collided);
        Assert.True(Vector2.Distance(before, inWall) > 3f);
    }

    [Fact]
    public void ResolveEntityCollision_WalkingThroughTheDoor_EntersTheHouse()
    {
        (ChunkWorld2D world, LandmarkPiece house) = FindHouse();
        Vector2 position = new(house.X * TileSize, (house.Y + 0.5f) * TileSize);

        // Walk north through the door opening in small steps, resolving collisions like the simulation does.
        for (int i = 0; i < 40; i++)
        {
            position += new Vector2(0f, -1.5f);
            _ = world.ResolveEntityCollision(ref position, radius: 7f, out _);
        }

        Assert.True(house.IsInside(position.X / TileSize, position.Y / TileSize), $"Ended at {position}, not inside the house.");
    }

    [Fact]
    public void ResolveEntityCollision_WalkingIntoTheSideWall_IsStoppedOutside()
    {
        (ChunkWorld2D world, LandmarkPiece house) = FindHouse();
        float left = (house.X - (house.Width * 0.5f)) * TileSize;
        Vector2 position = new(left - 20f, (house.Y - (house.Depth * 0.5f)) * TileSize);

        for (int i = 0; i < 30; i++)
        {
            position += new Vector2(1.5f, 0f);
            _ = world.ResolveEntityCollision(ref position, radius: 7f, out _);
        }

        Assert.True(position.X < left, "The side wall must stop an entity walking into it.");
    }

    [Fact]
    public void ResolveEntityCollision_InFrontOfHouse_IsNotBlockedByTheHouse()
    {
        (ChunkWorld2D world, LandmarkPiece house) = FindHouse();

        // Just south of the front wall: the roof overhang is above, but the ground is free.
        Vector2 inFront = new(house.X * TileSize, (house.Y * TileSize) + 10f);
        Vector2 before = inFront;
        _ = world.ResolveEntityCollision(ref inFront, radius: 7f, out _);

        Assert.True(inFront.Y >= before.Y - 0.01f, "The house must not pull or push an entity standing in front of it northwards.");
    }

    [Fact]
    public void Walkability_TileUnderHouse_IsImpassable()
    {
        (ChunkWorld2D world, LandmarkPiece house) = FindHouse();
        int tx = (int)MathF.Floor(house.X);
        int ty = (int)MathF.Floor(house.Y - 0.6f);

        Assert.Equal(TileWalkability.Impassable, world.NavGrid.GetWalkability(tx, ty));
    }

    [Fact]
    public void GetLayout_SameSeed_IsIdenticalAcrossGenerators()
    {
        ProceduralWorldGenerator a = new(42UL);
        ProceduralWorldGenerator b = new(42UL);
        for (int sy = -3; sy <= 3; sy++)
        {
            for (int sx = -3; sx <= 3; sx++)
            {
                Assert.True(a.Landmarks.GetLayout(sx, sy).Pieces.SequenceEqual(b.Landmarks.GetLayout(sx, sy).Pieces));
            }
        }
    }

    [Fact]
    public void GetLayout_Structures_StandOnDryGroundAtTheSiteLevel()
    {
        ProceduralWorldGenerator generator = new(42UL);
        for (int sy = -4; sy <= 4; sy++)
        {
            for (int sx = -4; sx <= 4; sx++)
            {
                PointOfInterest poi = generator.Landmarks.GetSite(sx, sy);
                if (poi.Type is PointOfInterestType.None)
                {
                    continue;
                }

                byte level = generator.SampleTile(poi.TileX, poi.TileY).HeightLevel;
                foreach (LandmarkPiece piece in generator.Landmarks.GetLayout(sx, sy).Pieces)
                {
                    if (piece.Kind is not (LandmarkPieceKind.House or LandmarkPieceKind.Warehouse or LandmarkPieceKind.Tent))
                    {
                        continue;
                    }

                    MapTile t = generator.SampleTile((int)MathF.Floor(piece.X), (int)MathF.Floor(piece.Y - 0.1f));
                    Assert.False(t.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean, $"{piece.Kind} in water at ({piece.X}, {piece.Y}).");
                    Assert.Equal(level, t.HeightLevel);
                }
            }
        }
    }

    private static (ChunkWorld2D World, LandmarkPiece House) FindHouse()
    {
        ProceduralWorldGenerator generator = new(42UL);
        for (int sy = -4; sy <= 4; sy++)
        {
            for (int sx = -4; sx <= 4; sx++)
            {
                foreach (LandmarkPiece piece in generator.Landmarks.GetLayout(sx, sy).Pieces)
                {
                    if (piece.Kind == LandmarkPieceKind.House)
                    {
                        ChunkWorld2D world = new(generator, TileSize);
                        world.UpdateAroundPosition(new Vector2(piece.X * TileSize, piece.Y * TileSize), chunkRadius: 1, evictionRadius: 3);
                        return (world, piece);
                    }
                }
            }
        }

        throw new InvalidOperationException("No house found near the origin.");
    }
}
