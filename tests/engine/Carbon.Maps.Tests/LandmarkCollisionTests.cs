// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Navigation;
using Carbon.Maps.Settlements;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

public sealed class LandmarkCollisionTests
{
    private const float TileSize = 32f;

    // Village houses are real building-grid structures (ADR-025): walls collide, a closed door blocks, an open one lets
    // characters in, and the ground in front of the house stays free.
    [Fact]
    public void ResolveEntityCollision_StandingInAWall_IsPushedOut()
    {
        (ChunkWorld2D world, VillagePlot plot, VillageBuildingTemplate template) = FindHouse();
        (int wx, int wy) = plot.ToWorld(0, template.Height / 2);
        Vector2 inWall = TileCenter(wx, wy);
        Vector2 before = inWall;
        bool collided = world.ResolveEntityCollision(ref inWall, radius: 7f, out _);

        Assert.True(collided);
        Assert.True(Vector2.Distance(before, inWall) > 3f);
    }

    [Fact]
    public void ResolveEntityCollision_WalkingThroughTheDoor_EntersTheHouseOnlyWhenOpen()
    {
        (ChunkWorld2D world, VillagePlot plot, VillageBuildingTemplate template) = FindHouse();
        (int dx, int dy) = VillageBlueprint.EntranceTile(plot);
        Vector2 start = TileCenter(dx, dy + 1);

        Vector2 closed = WalkNorth(world, start, steps: 48);
        Assert.True(closed.Y > dy * TileSize, "A closed door must stop an entity walking into it.");

        Assert.True(world.Buildings.ToggleDoor(dx, dy, out _));
        Vector2 open = WalkNorth(world, start, steps: 48);
        int insideY = (int)MathF.Floor(open.Y / TileSize);
        Assert.True(insideY < dy, $"Ended at {open}, not inside the house.");
        Assert.True(world.Buildings.IsRoofedAt(dx, insideY), "the interior of a village house is a roofed room");
        _ = template;
    }

    [Fact]
    public void ResolveEntityCollision_WalkingIntoTheSideWall_IsStoppedOutside()
    {
        (ChunkWorld2D world, VillagePlot plot, _) = FindHouse();
        float left = plot.OriginX * TileSize;
        Vector2 position = new(left - 20f, (plot.OriginY + (plot.Height * 0.5f)) * TileSize);

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
        (ChunkWorld2D world, VillagePlot plot, _) = FindHouse();
        (int fx, int fy) = VillageBlueprint.FrontTile(plot);
        Vector2 inFront = TileCenter(fx, fy + 1);
        Vector2 before = inFront;
        _ = world.ResolveEntityCollision(ref inFront, radius: 7f, out _);

        Assert.True(Vector2.Distance(before, inFront) < 0.01f, "The house must not push an entity standing in front of it.");
    }

    [Fact]
    public void Walkability_WallTile_IsImpassable()
    {
        (ChunkWorld2D world, VillagePlot plot, _) = FindHouse();
        (int tx, int ty) = plot.ToWorld(0, 0);

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

    private static Vector2 TileCenter(int x, int y) => new((x + 0.5f) * TileSize, (y + 0.5f) * TileSize);

    private static Vector2 WalkNorth(ChunkWorld2D world, Vector2 position, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            position += new Vector2(0f, -1.5f);
            _ = world.ResolveEntityCollision(ref position, radius: 7f, out _);
        }

        return position;
    }

    // A materialized village house whose entrance faces south.
    private static (ChunkWorld2D World, VillagePlot Plot, VillageBuildingTemplate Template) FindHouse()
    {
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) in VillageGenerationTests.Villages(42UL))
        {
            foreach (VillagePlot plot in layout.Plots)
            {
                if (plot.Role != VillageBuildingRole.House || plot.Turns != 0)
                {
                    continue;
                }

                ChunkWorld2D world = new(gen, TileSize);
                world.UpdateAroundPosition(new Vector2(poi.TileX * TileSize, poi.TileY * TileSize), chunkRadius: 1, evictionRadius: 3);
                (int sx, int sy) = VillageMaterializer.SiteOfChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
                _ = VillageGenerationTests.NewMaterializer(world).Materialize(sx, sy);
                return (world, plot, plot.Template);
            }
        }

        throw new InvalidOperationException("No south-facing village house found near the origin.");
    }
}
