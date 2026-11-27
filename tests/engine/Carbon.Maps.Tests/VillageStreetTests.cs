// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Settlements;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

// ADR-027 revision 3: streets lead somewhere and are paved with a player-buildable floor from the square to every door.
public sealed class VillageStreetTests
{
    private static readonly (int X, int Y)[] s_cardinal = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    // A lane with no building on it used to run on to the beach (the desert "fan" of door paths and empty lanes).
    [Theory]
    [InlineData(42UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1337UL)]
    public void Plan_Streets_EveryStreetLeadsToABuildingAndStopsPastItsLastJunction(ulong seed)
    {
        int streets = 0;
        foreach ((_, PointOfInterest poi, LandmarkLayout layout) in VillageGenerationTests.Villages(seed))
        {
            ReadOnlySpan<LandmarkPath> paths = layout.Paths;
            for (int i = 0; i < paths.Length; i++)
            {
                LandmarkPath street = paths[i];
                if (VillageGenerationTests.DoorPathOwner(layout, street) >= 0)
                {
                    continue;
                }

                streets++;
                float last = -1f;
                for (int k = 0; k < paths.Length; k++)
                {
                    LandmarkPath other = paths[k];
                    bool doorPath = VillageGenerationTests.DoorPathOwner(layout, other) >= 0;
                    float along = doorPath ? Along(street, other.X1, other.Y1) : k == i ? -1f : Along(street, other.X0, other.Y0);
                    if (!doorPath && along < 0.5f)
                    {
                        continue; // another main arm starting at the square is not a junction
                    }

                    last = MathF.Max(last, along);
                }

                float length = MathF.Sqrt(((street.X1 - street.X0) * (street.X1 - street.X0)) + ((street.Y1 - street.Y0) * (street.Y1 - street.Y0)));
                Assert.True(last >= 0f, $"a street of {poi.TileX},{poi.TileY} from {street.X0:F1},{street.Y0:F1} leads to no building");
                Assert.True(length - last <= 1.05f, $"a street of {poi.TileX},{poi.TileY} runs {length - last:F1} tiles past its last junction");
            }
        }

        Assert.True(streets > 100, $"only {streets} streets");
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1337UL)]
    public void Plan_PavedTiles_FormOneNetworkFromTheSquareToEveryDoorAlongEveryPath(ulong seed)
    {
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) in VillageGenerationTests.Villages(seed))
        {
            HashSet<(int X, int Y)> paved = [];
            foreach ((int X, int Y) tile in layout.PavedTiles)
            {
                Assert.True(paved.Add(tile), $"{tile} is paved twice");
            }

            // The square, every front step, and every dry tile under a street or door path centre line outside plots and gardens.
            Assert.Contains((poi.TileX, poi.TileY), paved);
            foreach (VillagePlot plot in layout.Plots)
            {
                Assert.Contains(VillageBlueprint.FrontTile(plot), paved);
            }

            byte level = gen.SampleTile(poi.TileX, poi.TileY).HeightLevel;
            foreach (LandmarkPath path in layout.Paths)
            {
                float length = MathF.Sqrt(((path.X1 - path.X0) * (path.X1 - path.X0)) + ((path.Y1 - path.Y0) * (path.Y1 - path.Y0)));
                int steps = Math.Max(1, (int)MathF.Ceiling(length * 4f));
                for (int k = 0; k <= steps; k++)
                {
                    int x = (int)MathF.Floor(path.X0 + ((path.X1 - path.X0) * k / steps));
                    int y = (int)MathF.Floor(path.Y0 + ((path.Y1 - path.Y0) * k / steps));
                    MapTile t = gen.SampleTile(x, y);
                    bool dry = !t.IsSolid && t.HeightLevel == level && t.Biome is not (Biomes.BiomeType.ShallowWater or Biomes.BiomeType.DeepOcean);
                    if (dry && !InPlotOrGarden(layout, x, y))
                    {
                        Assert.Contains((x, y), paved);
                    }
                }
            }

            // One 4-connected network: a player walks the paving from the well to every door without stepping on grass. A
            // building's apron (porch, paved yard or frontage) joins its front step to the street.
            HashSet<(int X, int Y)> walkable = [.. paved];
            foreach (VillagePlot plot in layout.Plots)
            {
                for (int y = plot.OriginY; y < plot.OriginY + plot.Height; y++)
                {
                    for (int x = plot.OriginX; x < plot.OriginX + plot.Width; x++)
                    {
                        if (plot.CellAtWorld(x, y) is VillageBuildingTemplate.PorchCell or VillageBuildingTemplate.YardCell)
                        {
                            _ = walkable.Add((x, y));
                        }
                    }
                }
            }

            HashSet<(int X, int Y)> reached = [(poi.TileX, poi.TileY)];
            Queue<(int X, int Y)> queue = new([(poi.TileX, poi.TileY)]);
            while (queue.Count > 0)
            {
                (int x, int y) = queue.Dequeue();
                foreach ((int dx, int dy) in s_cardinal)
                {
                    if (walkable.Contains((x + dx, y + dy)) && reached.Add((x + dx, y + dy)))
                    {
                        queue.Enqueue((x + dx, y + dy));
                    }
                }
            }

            paved.ExceptWith(reached);
            Assert.True(paved.Count == 0, $"{paved.Count} paved tiles of {poi.TileX},{poi.TileY} are cut off from the square");
        }
    }

    [Fact]
    public void Materialize_PavedTiles_AreBuildableVillageFloorsInTheStyleOfTheVillage()
    {
        StructureCatalog catalog = StructureCatalog.LoadEmbedded();
        int checkedTiles = 0;
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) in VillageGenerationTests.Villages(42UL))
        {
            if (layout.Plots.Length == 0 || checkedTiles > 2000)
            {
                continue;
            }

            ChunkWorld2D world = new(gen);
            VillageMaterializer materializer = VillageGenerationTests.NewMaterializer(world);
            (int sx, int sy) = VillageMaterializer.SiteOfChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
            _ = materializer.Materialize(sx, sy);
            StructureType paving = VillageBlueprint.PavingType(VillageBuildingCatalog.Default, layout.Plots[0].Style);
            foreach ((int x, int y) in layout.PavedTiles)
            {
                Assert.True(world.Buildings.TryGetStructure(x, y, out StructureTile tile), $"{x},{y} of {poi.TileX},{poi.TileY} is not paved");
                Assert.Equal(StructureCategory.Floor, tile.Category);
                Assert.True(catalog.TryGet(tile.Type, out StructureDefinition def) && def.Cost.Count > 0, $"{tile.Type} is not a buildable catalog floor");
                Assert.True(world.Construction.TryGetRecord(x, y, out StructureRecord record));
                Assert.Equal(StructureOwners.Village, record.OwnerId);
                if (!InPlotOrGarden(layout, x, y))
                {
                    Assert.Equal(paving, tile.Type);
                }

                checkedTiles++;
            }
        }

        Assert.True(checkedTiles > 500, $"only {checkedTiles} paved tiles checked");
    }

    private static float Along(in LandmarkPath street, float x, float y)
    {
        if (street.DistanceSquaredTo(x, y) > 0.01f)
        {
            return -1f;
        }

        return MathF.Sqrt(((x - street.X0) * (x - street.X0)) + ((y - street.Y0) * (y - street.Y0)));
    }

    private static bool InPlotOrGarden(LandmarkLayout layout, int x, int y)
    {
        foreach (VillagePlot plot in layout.Plots)
        {
            if (x >= plot.OriginX && x < plot.OriginX + plot.Width && y >= plot.OriginY && y < plot.OriginY + plot.Height)
            {
                return true;
            }
        }

        foreach (LandmarkField field in layout.Fields)
        {
            if (x >= field.MinX && x < field.MaxX && y >= field.MinY && y < field.MaxY)
            {
                return true;
            }
        }

        return false;
    }
}
