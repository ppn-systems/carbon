// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Settlements;
using Carbon.Maps.Streaming;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Maps.Tests;

public sealed class VillageGenerationTests
{
    // Pins every village plot (generated footprint, furniture, origin, turns) and every paved tile of seed 42 over a 13x13
    // site window (generator version 21: streets pruned to the buildings they serve and paved with a buildable floor,
    // ADR-027 revision 3). Update it only together with GeneratorVersion.
    private const ulong GoldenVillagePlotFingerprint = 4882946201328673321UL;

    private readonly ITestOutputHelper _output;

    public VillageGenerationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Catalog_Parse_RejectsBadPalettes()
    {
        const string Ok = """{ "wall": "WoodWall", "door": "WoodDoor", "floor": "WoodFloor" }""";
        static string Doc(string temperate, string desert = Ok) =>
            "{ \"styles\": { \"temperate\": " + temperate + ", \"desert\": " + desert + ", \"snow\": " + Ok + ", \"swamp\": " + Ok + " } }";

        Assert.NotNull(VillageBuildingCatalog.Parse(Doc(Ok)));
        Assert.Throws<FormatException>(() => VillageBuildingCatalog.Parse(Doc("""{ "wall": "WoodFloor", "door": "WoodDoor", "floor": "WoodFloor" }""")));
        Assert.Throws<FormatException>(() => VillageBuildingCatalog.Parse(Doc("""{ "wall": "Nope", "door": "WoodDoor", "floor": "WoodFloor" }""")));
        Assert.Throws<FormatException>(() => VillageBuildingCatalog.Parse("""{ "styles": { "temperate": { "wall": "WoodWall", "door": "WoodDoor", "floor": "WoodFloor" } } }"""));
    }

    [Fact]
    public void Plan_SameSeed_IsIdenticalAcrossGenerators()
    {
        Assert.Equal(Fingerprint(new ProceduralWorldGenerator(42UL)), Fingerprint(new ProceduralWorldGenerator(42UL)));
        Assert.NotEqual(Fingerprint(new ProceduralWorldGenerator(42UL)), Fingerprint(new ProceduralWorldGenerator(43UL)));
    }

    [Fact]
    public void Plan_GoldenFingerprint_IsStable()
    {
        ulong h = Fingerprint(new ProceduralWorldGenerator(42UL));
        _output.WriteLine($"Village plot fingerprint: {h}UL");
        Assert.Equal(GoldenVillagePlotFingerprint, h);
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1337UL)]
    public void Plan_Villages_GetStallSmithyStoreAndHouses(ulong seed)
    {
        int villages = 0;
        int complete = 0;
        Dictionary<PointOfInterestType, int> byType = new();
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) in Villages(seed))
        {
            _ = gen;
            villages++;
            byType[poi.Type] = byType.GetValueOrDefault(poi.Type) + 1;
            bool stall = false, smithy = false, store = false, storehouse = false;
            int houses = 0;
            foreach (VillagePlot plot in layout.Plots)
            {
                stall |= plot.Role == VillageBuildingRole.Stall;
                smithy |= plot.Role == VillageBuildingRole.Smithy;
                store |= plot.Role == VillageBuildingRole.Store;
                storehouse |= plot.Role == VillageBuildingRole.Storehouse;
                houses += plot.Role == VillageBuildingRole.House ? 1 : 0;
            }

            if (stall && smithy && store && storehouse && houses >= 3)
            {
                complete++;
            }

            Assert.True(layout.Plots.Length >= 3, $"{poi.Type} at {poi.TileX},{poi.TileY} has only {layout.Plots.Length} buildings.");
        }

        _output.WriteLine($"{villages} villages, {complete} complete; {string.Join(", ", byType)}");
        Assert.True(villages >= 8, $"Only {villages} villages found.");
        Assert.True(complete * 10 >= villages * 9, $"Only {complete} of {villages} villages have stall, smithy, store, storehouse and 3+ houses.");
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(2UL)]
    public void Plan_Plots_StayInsideTheFootprintWithTwoTilesBetweenBuildings(ulong seed)
    {
        foreach ((_, PointOfInterest poi, LandmarkLayout layout) in Villages(seed))
        {
            ReadOnlySpan<VillagePlot> plots = layout.Plots;
            for (int i = 0; i < plots.Length; i++)
            {
                VillagePlot a = plots[i];
                Assert.True(poi.Contains(a.OriginX, a.OriginY) && poi.Contains(a.OriginX + a.Width - 1, a.OriginY + a.Height - 1));
                for (int j = i + 1; j < plots.Length; j++)
                {
                    VillagePlot b = plots[j];
                    // At least two tiles of open ground between any two buildings (yards and alleys, ADR-027).
                    bool apart = a.OriginX + a.Width + 2 <= b.OriginX || b.OriginX + b.Width + 2 <= a.OriginX ||
                        a.OriginY + a.Height + 2 <= b.OriginY || b.OriginY + b.Height + 2 <= a.OriginY;
                    Assert.True(apart, $"Plots {i} and {j} of the village at {poi.TileX},{poi.TileY} are closer than two tiles.");
                }
            }
        }
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1337UL)]
    public void Materialize_Buildings_AreEnclosedRoomsWithReachableDoorsAndNpcSpots(ulong seed)
    {
        int checkedBuildings = 0;
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) in Villages(seed))
        {
            ChunkWorld2D world = new(gen);
            VillageMaterializer materializer = NewMaterializer(world);
            (int sx, int sy) = VillageMaterializer.SiteOfChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
            int placed = materializer.Materialize(sx, sy);
            Assert.True(placed > 50, $"Village at {poi.TileX},{poi.TileY} placed only {placed} tiles.");

            foreach (VillagePlot plot in layout.Plots)
            {
                VillageBuildingTemplate template = plot.Template;
                (int fx, int fy) = VillageBlueprint.FrontTile(plot);
                HashSet<(int, int)> reach = FloodWalkable(world, fx, fy, plot);
                for (int y = 0; y < template.Height; y++)
                {
                    for (int x = 0; x < template.Width; x++)
                    {
                        if (template.CellAt(x, y) != VillageBuildingTemplate.FloorCell)
                        {
                            continue;
                        }

                        (int wx, int wy) = plot.ToWorld(x, y);
                        Assert.True(
                            RoomDetector.IsTileEnclosed(wx, wy, world.Buildings.IsRoomBoundaryAt),
                            $"{template.Id} cell {x},{y} at {wx},{wy} is not enclosed.");
                        if (!world.Buildings.IsSolidAt(wx, wy))
                        {
                            Assert.True(reach.Contains((wx, wy)), $"{template.Id} cell {x},{y} cannot be reached from its door.");
                        }
                    }
                }

                if (VillageBlueprint.TryGetNpcTile(plot, out int nx, out int ny))
                {
                    Assert.False(world.Buildings.IsSolidAt(nx, ny), $"{template.Id} NPC spot is blocked.");
                    Assert.True(reach.Contains((nx, ny)), $"{template.Id} NPC spot cannot be reached from the entrance.");
                }

                checkedBuildings++;
            }
        }

        Assert.True(checkedBuildings > 20);
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1337UL)]
    public void Plan_BuildingsOfOneVillage_AllHaveDifferentFootprints(ulong seed)
    {
        foreach ((_, PointOfInterest poi, LandmarkLayout layout) in Villages(seed))
        {
            ReadOnlySpan<VillagePlot> plots = layout.Plots;
            for (int i = 0; i < plots.Length; i++)
            {
                for (int j = i + 1; j < plots.Length; j++)
                {
                    Assert.False(
                        plots[i].Template.HasSameFootprint(plots[j].Template),
                        $"{plots[i].Template.Id} and {plots[j].Template.Id} of the village at {poi.TileX},{poi.TileY} share a footprint");
                }
            }
        }
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1337UL)]
    public void Plan_Buildings_StandOnDryGroundAtTheSiteLevelAndOffStreetsAndRoads(ulong seed)
    {
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) in Villages(seed))
        {
            byte level = gen.SampleTile(poi.TileX, poi.TileY).HeightLevel;
            for (int index = 0; index < layout.Plots.Length; index++)
            {
                VillagePlot plot = layout.Plots[index];
                for (int y = plot.OriginY - 1; y <= plot.OriginY + plot.Height; y++)
                {
                    for (int x = plot.OriginX - 1; x <= plot.OriginX + plot.Width; x++)
                    {
                        MapTile t = gen.SampleTile(x, y);
                        bool dry = !t.IsSolid && t.HeightLevel == level && t.Biome is not (Biomes.BiomeType.ShallowWater or Biomes.BiomeType.DeepOcean);
                        Assert.True(dry, $"{plot.Template.Id} at {poi.TileX},{poi.TileY} stands on water or a cliff at {x},{y}");
                        if (plot.Contains(x, y))
                        {
                            Assert.False(gen.Roads.IsRoadOrBridgeAtTile(x, y, bridgeOnly: false), $"{plot.Template.Id} is built over a road at {x},{y}");
                        }
                    }
                }

                foreach (LandmarkPath path in layout.Paths)
                {
                    if (DoorPathOwner(layout, path) == index)
                    {
                        continue; // The path to a door crosses its own porch or yard, never another building.
                    }

                    for (int y = plot.OriginY; y < plot.OriginY + plot.Height; y++)
                    {
                        for (int x = plot.OriginX; x < plot.OriginX + plot.Width; x++)
                        {
                            float reach = path.Width * 0.5f;
                            Assert.True(
                                path.DistanceSquaredTo(x + 0.5f, y + 0.5f) > reach * reach,
                                $"{plot.Template.Id} at {poi.TileX},{poi.TileY} is built over a street or another building's door path at {x},{y}");
                        }
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1337UL)]
    public void Plan_EveryDoor_FacesAPathThatLeadsToAStreet(ulong seed)
    {
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) in Villages(seed))
        {
            List<LandmarkPath> streets = [];
            foreach (LandmarkPath path in layout.Paths)
            {
                if (!IsDoorPath(layout, path))
                {
                    streets.Add(path);
                }
            }

            Assert.NotEmpty(streets);
            foreach (VillagePlot plot in layout.Plots)
            {
                (int fx, int fy) = VillageBlueprint.FrontTile(plot);
                (int ex, int ey) = VillageBlueprint.EntranceTile(plot);
                float doorX = fx - ex;
                float doorY = fy - ey;
                bool served = false;
                foreach (LandmarkPath spur in layout.Paths)
                {
                    bool startsAtDoor = MathF.Abs(spur.X0 - (fx + 0.5f)) < 0.01f && MathF.Abs(spur.Y0 - (fy + 0.5f)) < 0.01f;
                    if (!startsAtDoor)
                    {
                        continue;
                    }

                    float dx = spur.X1 - spur.X0;
                    float dy = spur.Y1 - spur.Y0;
                    float length = MathF.Sqrt((dx * dx) + (dy * dy));
                    bool onStreet = streets.Exists(s => s.DistanceSquaredTo(spur.X1, spur.Y1) < 0.01f);
                    bool outwards = length < 0.01f || ((dx * doorX) + (dy * doorY)) / length >= 0.5f;
                    served |= onStreet && outwards;
                }

                Assert.True(served, $"{plot.Template.Id} at {poi.TileX},{poi.TileY}: no path leads from its door straight out to a street");
            }

            _ = gen;
        }
    }

    // A ring of buildings around the well turns every entrance to the well. Buildings that line streets face the street,
    // which runs through the square, so their doors mostly look past the well, not at it.
    [Theory]
    [InlineData(42UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1337UL)]
    public void Plan_Layout_IsNotARingFacingTheWell(ulong seed)
    {
        double sum = 0;
        int buildings = 0;
        foreach ((_, PointOfInterest poi, LandmarkLayout layout) in Villages(seed))
        {
            foreach (VillagePlot plot in layout.Plots)
            {
                if (plot.Role == VillageBuildingRole.Stall)
                {
                    continue;
                }

                (int fx, int fy) = VillageBlueprint.FrontTile(plot);
                (int ex, int ey) = VillageBlueprint.EntranceTile(plot);
                double wx = poi.TileX - ex;
                double wy = poi.TileY - ey;
                double length = Math.Sqrt((wx * wx) + (wy * wy));
                sum += (((fx - ex) * wx) + ((fy - ey) * wy)) / Math.Max(length, 1e-6);
                buildings++;
            }
        }

        double mean = sum / buildings;
        _output.WriteLine($"{buildings} buildings, mean door-to-well alignment {mean:F2}");
        Assert.True(buildings > 100, $"only {buildings} buildings");
        Assert.True(mean < 0.7, $"doors point at the well on average ({mean:F2}): the layout is still a ring around the square");
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(1337UL)]
    public void Materialize_EveryDoorAndNpcSpot_IsReachableOnFootFromTheSquare(ulong seed)
    {
        int doors = 0;
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) in Villages(seed))
        {
            ChunkWorld2D world = new(gen);
            (int sx, int sy) = VillageMaterializer.SiteOfChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
            _ = NewMaterializer(world).Materialize(sx, sy);
            HashSet<(int, int)> reach = FloodFromSquare(world, gen, poi);
            foreach (VillagePlot plot in layout.Plots)
            {
                for (int y = 0; y < plot.Template.Height; y++)
                {
                    for (int x = 0; x < plot.Template.Width; x++)
                    {
                        if (plot.Template.CellAt(x, y) != VillageBuildingTemplate.DoorCell)
                        {
                            continue;
                        }

                        (int wx, int wy) = plot.ToWorld(x, y);
                        Assert.True(reach.Contains((wx, wy)), $"{plot.Template.Id} door {x},{y} at {wx},{wy} cannot be reached from the square of {poi.TileX},{poi.TileY}");
                        doors++;
                    }
                }

                if (VillageBlueprint.TryGetNpcTile(plot, out int nx, out int ny))
                {
                    Assert.True(reach.Contains((nx, ny)), $"{plot.Template.Id} NPC spot cannot be reached from the square of {poi.TileX},{poi.TileY}");
                }
            }
        }

        Assert.True(doors > 100, $"only {doors} doors checked");
    }

    // Villages are built only from what a player can build: every tile is a structures.json definition with a build
    // cost, furniture stands at its catalog footprint for its rotation, and every door sits between walls, as the build
    // tool requires (ADR-027). A positive check against the catalog: no list of allowed types to keep in step.
    [Theory]
    [InlineData(42UL)]
    [InlineData(1337UL)]
    public void Plan_EveryVillageStructure_IsAPlayerBuildableCatalogStructure(ulong seed)
    {
        StructureCatalog catalog = StructureCatalog.LoadEmbedded();
        List<VillagePlacement> placements = [];
        int checkedTiles = 0;
        foreach ((_, PointOfInterest poi, LandmarkLayout layout) in Villages(seed))
        {
            foreach (VillagePlot plot in layout.Plots)
            {
                placements.Clear();
                VillageBlueprint.Expand(VillageBuildingCatalog.Default, catalog, plot, placements);
                HashSet<(int, int)> walls = [];
                foreach (VillagePlacement p in placements)
                {
                    Assert.True(catalog.TryGet(p.Type, out StructureDefinition def), $"{plot.Template.Id} at {poi.TileX},{poi.TileY} emits {p.Type}, which is not in structures.json");
                    Assert.NotEmpty(def.Cost);
                    (int fw, int fh) = def.GetFootprint(p.Rotation);
                    Assert.InRange(p.X - p.AnchorX, 0, fw - 1);
                    Assert.InRange(p.Y - p.AnchorY, 0, fh - 1);
                    if (def.Category is StructureCategory.Wall or StructureCategory.Door or StructureCategory.Pillar)
                    {
                        _ = walls.Add((p.X, p.Y));
                    }

                    checkedTiles++;
                }

                // A multi-tile piece covers exactly its catalog footprint.
                foreach (IGrouping<(int, int, StructureType), VillagePlacement> piece in placements.GroupBy(p => (p.AnchorX, p.AnchorY, p.Type)))
                {
                    VillagePlacement first = piece.First();
                    (int fw, int fh) = catalog.TryGet(first.Type, out StructureDefinition def) ? def.GetFootprint(first.Rotation) : (1, 1);
                    Assert.Equal(fw * fh, piece.Count());
                }

                foreach (VillagePlacement door in placements.Where(p => catalog.TryGet(p.Type, out StructureDefinition d) && d.RequiresWallsOnSides))
                {
                    bool eastWest = walls.Contains((door.X - 1, door.Y)) && walls.Contains((door.X + 1, door.Y));
                    bool northSouth = walls.Contains((door.X, door.Y - 1)) && walls.Contains((door.X, door.Y + 1));
                    Assert.True(eastWest || northSouth, $"{plot.Template.Id} door at {door.X},{door.Y} is not set between walls");
                }
            }
        }

        Assert.True(checkedTiles > 10_000, $"only {checkedTiles} tiles checked");
    }

    // The owner asked for the roads between villages to go: no road or bridge crosses any village (ADR-027).
    [Theory]
    [InlineData(42UL)]
    [InlineData(1UL)]
    [InlineData(1337UL)]
    public void Plan_Villages_HaveNoRoadOrBridgeTiles(ulong seed)
    {
        int villages = 0;
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, _) in Villages(seed))
        {
            int r = poi.Radius;
            bool[] roads = new bool[((2 * r) + 1) * ((2 * r) + 1)];
            gen.Roads.Rasterize(poi.TileX - r, poi.TileY - r, (2 * r) + 1, (2 * r) + 1, roads);
            for (int i = 0; i < roads.Length; i++)
            {
                int x = poi.TileX - r + (i % ((2 * r) + 1));
                int y = poi.TileY - r + (i / ((2 * r) + 1));
                Assert.False(roads[i] && poi.Contains(x, y), $"a road crosses the {poi.Type} at {poi.TileX},{poi.TileY} at tile {x},{y}");
            }

            villages++;
        }

        Assert.True(villages >= 30, $"only {villages} villages");
    }

    // Homes get kitchen gardens beside them, on dry ground, clear of every building and street.
    [Theory]
    [InlineData(42UL)]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(1337UL)]
    [InlineData(5UL)]
    [InlineData(7UL)]
    public void Plan_Gardens_LieBesideHomesClearOfBuildingsAndStreets(ulong seed)
    {
        int gardens = 0;
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) in Villages(seed))
        {
            byte level = gen.SampleTile(poi.TileX, poi.TileY).HeightLevel;
            foreach (LandmarkField field in layout.Fields)
            {
                gardens++;
                int x0 = (int)field.MinX;
                int y0 = (int)field.MinY;
                int x1 = (int)field.MaxX - 1;
                int y1 = (int)field.MaxY - 1;
                bool besideHome = false;
                foreach (VillagePlot plot in layout.Plots)
                {
                    Assert.False(x0 <= plot.OriginX + plot.Width - 1 && x1 >= plot.OriginX && y0 <= plot.OriginY + plot.Height - 1 && y1 >= plot.OriginY, $"a garden at {x0},{y0} overlaps {plot.Template.Id}");
                    besideHome |= plot.Role == VillageBuildingRole.House &&
                        x0 <= plot.OriginX + plot.Width + 1 && x1 >= plot.OriginX - 2 && y0 <= plot.OriginY + plot.Height + 1 && y1 >= plot.OriginY - 2;
                }

                Assert.True(besideHome, $"the garden at {x0},{y0} of {poi.TileX},{poi.TileY} is not beside a home");
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        MapTile t = gen.SampleTile(x, y);
                        Assert.True(!t.IsSolid && t.HeightLevel == level && t.Biome is not (Biomes.BiomeType.ShallowWater or Biomes.BiomeType.DeepOcean), $"garden tile {x},{y} is not dry level ground");
                        foreach (LandmarkPath path in layout.Paths)
                        {
                            Assert.True(path.DistanceSquaredTo(x + 0.5f, y + 0.5f) > path.Width * path.Width * 0.25f, $"garden tile {x},{y} lies on a path");
                        }
                    }
                }
            }
        }

        Assert.True(gardens > 50, $"only {gardens} gardens");
    }

    [Fact]
    public void Materialize_SecondRun_PlacesNothingAndEveryTileIsVillageOwned()
    {
        (ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) = FirstVillage();
        ChunkWorld2D world = new(gen);
        VillageMaterializer materializer = NewMaterializer(world);
        (int sx, int sy) = VillageMaterializer.SiteOfChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
        HashSet<long> touched = [];
        int first = materializer.Materialize(sx, sy, touched);

        Assert.True(first > 0);
        Assert.NotEmpty(touched);
        Assert.Equal(0, materializer.Materialize(sx, sy));
        Assert.Equal(first, world.Buildings.StructureCount);
        Assert.Equal(first, world.Construction.RecordCount);
        foreach (KeyValuePair<(int X, int Y), StructureTile> kvp in world.Buildings.PlacedStructures)
        {
            Assert.True(world.Construction.TryGetRecord(kvp.Key.X, kvp.Key.Y, out StructureRecord record));
            Assert.Equal(StructureOwners.Village, record.OwnerId);
        }

        Assert.True(layout.Plots.Length > 0);
    }

    [Fact]
    public void Materialize_SameSeed_PlacesIdenticalStructures() =>
        Assert.Equal(StructureFingerprint(42UL), StructureFingerprint(42UL));

    [Fact]
    public void Materialize_WrittenToChunkDeltas_SurviveSerializationRoundTrip()
    {
        (ProceduralWorldGenerator gen, PointOfInterest poi, _) = FirstVillage();
        ChunkWorld2D world = new(gen);
        (int sx, int sy) = VillageMaterializer.SiteOfChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
        _ = NewMaterializer(world).Materialize(sx, sy);

        ChunkWorld2D restored = new(new ProceduralWorldGenerator(gen.Seed));
        foreach (ChunkDelta delta in world.Deltas)
        {
            restored.ImportDelta(ChunkDelta.Deserialize(delta.Serialize()));
        }

        Assert.Equal(world.Buildings.StructureCount, restored.Buildings.StructureCount);
        Assert.Equal(world.Construction.RecordCount, restored.Construction.RecordCount);
        foreach (KeyValuePair<(int X, int Y), StructureTile> kvp in world.Buildings.PlacedStructures)
        {
            Assert.True(restored.Buildings.TryGetStructure(kvp.Key.X, kvp.Key.Y, out StructureTile tile));
            Assert.Equal(kvp.Value.Type, tile.Type);
            Assert.True(restored.Construction.TryGetRecord(kvp.Key.X, kvp.Key.Y, out StructureRecord record));
            Assert.Equal(StructureOwners.Village, record.OwnerId);
        }
    }

    [Fact]
    public void MigrateTo_NewGeneratorVersion_DropsVillageStructuresButKeepsPlayerBuilds()
    {
        (ProceduralWorldGenerator gen, PointOfInterest poi, _) = FirstVillage();
        ChunkWorld2D world = new(gen);
        (int sx, int sy) = VillageMaterializer.SiteOfChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
        _ = NewMaterializer(world).Materialize(sx, sy);
        ChunkDelta village = world.Deltas[0];
        int lx = 0;
        int ly = 0;
        while (village.Structures.ContainsKey((ly * Chunk2D.Size) + lx))
        {
            lx++;
        }

        StructureTile player = BuildingGrid.CreateBlueprint(StructureType.StoneWall);
        village.SetStructure(lx, ly, in player);

        ChunkDelta migrated = village.MigrateTo(ProceduralWorldGenerator.GeneratorVersion + 1);

        Assert.Single(migrated.Structures);
        Assert.Empty(migrated.StructureRecords);
    }

    [Fact]
    public void BuildingService_PlayerActions_CannotModifyVillageStructures()
    {
        (ProceduralWorldGenerator gen, PointOfInterest poi, LandmarkLayout layout) = FirstVillage();
        ChunkWorld2D world = new(gen);
        (int sx, int sy) = VillageMaterializer.SiteOfChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
        _ = NewMaterializer(world).Materialize(sx, sy);
        BuildingService service = BuildingService.ForWorld(world, StructureCatalog.LoadEmbedded(), new BuildPermissionTable());

        VillagePlot plot = layout.Plots[0].Role == VillageBuildingRole.Stall ? layout.Plots[1] : layout.Plots[0];
        VillageBuildingTemplate template = plot.Template;
        (int wallX, int wallY) = plot.ToWorld(0, 0);
        BuildActor player = new(12345UL, wallX, wallY + 1, ToolTier: 9);
        FakeInventory refunds = new();

        Assert.Equal(BuildResultCode.ProtectedSettlement, service.ApplyDeconstructionWork(player, wallX, wallY, 1_000_000f, refunds).Code);
        Assert.Equal(BuildResultCode.ProtectedSettlement, service.ApplyPlayerDamage(player.ActorId, wallX, wallY, 1_000_000f, null).Code);
        Assert.True(world.Buildings.TryGetStructure(wallX, wallY, out StructureTile wall) && !wall.IsDestroyed, "the wall must be untouched");

        // No furniture or blueprint may be placed on a village floor.
        for (int y = 0; y < template.Height; y++)
        {
            for (int x = 0; x < template.Width; x++)
            {
                (int fx, int fy) = plot.ToWorld(x, y);
                if (template.CellAt(x, y) == VillageBuildingTemplate.FloorCell &&
                    world.Buildings.TryGetStructure(fx, fy, out StructureTile floor) && floor.Category == StructureCategory.Floor)
                {
                    Assert.Equal(BuildResultCode.ProtectedSettlement, service.ValidatePlacement(fx, fy, StructureType.WoodChest, StructureRotation.North));
                    return;
                }
            }
        }

        Assert.Fail("No village floor found.");
    }

    [Fact]
    public void BuildPermissionTable_FactionOwner_IsClosedToPlayers()
    {
        BuildPermissionTable table = new();
        Assert.False(table.CanModify(42UL, StructureOwners.Village));
        Assert.True(table.CanModify(StructureOwners.Village, StructureOwners.Village));
        Assert.False(StructureOwners.IsFaction(StructureOwners.ToPlayerOwnerId(ulong.MaxValue)));
        Assert.NotEqual(0UL, StructureOwners.ToPlayerOwnerId(0UL));
    }

    internal static VillageMaterializer NewMaterializer(ChunkWorld2D world) =>
        new(world, VillageBuildingCatalog.Default, StructureCatalog.LoadEmbedded());

    internal static IEnumerable<(ProceduralWorldGenerator Generator, PointOfInterest Poi, LandmarkLayout Layout)> Villages(ulong seed)
    {
        ProceduralWorldGenerator gen = new(seed);
        for (int sy = -8; sy <= 8; sy++)
        {
            for (int sx = -8; sx <= 8; sx++)
            {
                PointOfInterest poi = gen.Landmarks.GetSite(sx, sy);
                if (poi.Type is PointOfInterestType.RiverVillage or PointOfInterestType.DesertVillage or
                    PointOfInterestType.SnowVillage or PointOfInterestType.SwampVillage)
                {
                    yield return (gen, poi, gen.Landmarks.GetLayout(sx, sy));
                }
            }
        }
    }

    private static (ProceduralWorldGenerator Generator, PointOfInterest Poi, LandmarkLayout Layout) FirstVillage()
    {
        foreach ((ProceduralWorldGenerator Generator, PointOfInterest Poi, LandmarkLayout Layout) v in Villages(42UL))
        {
            if (v.Layout.Plots.Length >= 3)
            {
                return v;
            }
        }

        throw new InvalidOperationException("No village found.");
    }

    private static ulong Fingerprint(ProceduralWorldGenerator gen)
    {
        ulong h = 1469598103934665603UL;
        void Mix(ulong v) => h = (h ^ v) * 1099511628211UL;
        for (int sy = -6; sy <= 6; sy++)
        {
            for (int sx = -6; sx <= 6; sx++)
            {
                foreach (VillagePlot p in gen.Landmarks.GetLayout(sx, sy).Plots)
                {
                    Mix((ulong)p.Role);
                    for (int y = 0; y < p.Template.Height; y++)
                    {
                        for (int x = 0; x < p.Template.Width; x++)
                        {
                            Mix(p.Template.CellAt(x, y));
                        }
                    }

                    foreach (VillageFurniture f in p.Template.Furniture)
                    {
                        Mix(((ulong)f.Type << 32) | ((ulong)(uint)f.X << 16) | (uint)f.Y);
                    }
                    Mix((ulong)p.Style);
                    Mix((uint)p.OriginX);
                    Mix((uint)p.OriginY);
                    Mix(p.Turns);
                }

                foreach ((int x, int y) in gen.Landmarks.GetLayout(sx, sy).PavedTiles)
                {
                    Mix(((ulong)(uint)x << 32) | (uint)y);
                }
            }
        }

        return h;
    }

    private static ulong StructureFingerprint(ulong seed)
    {
        ulong h = 1469598103934665603UL;
        foreach ((ProceduralWorldGenerator gen, PointOfInterest poi, _) in Villages(seed))
        {
            ChunkWorld2D world = new(gen);
            (int sx, int sy) = VillageMaterializer.SiteOfChunk(Chunk2D.ToChunkCoord(poi.TileX), Chunk2D.ToChunkCoord(poi.TileY));
            _ = NewMaterializer(world).Materialize(sx, sy);
            List<(int X, int Y, byte T, byte C)> tiles = [];
            foreach (KeyValuePair<(int X, int Y), StructureTile> kvp in world.Buildings.PlacedStructures)
            {
                tiles.Add((kvp.Key.X, kvp.Key.Y, (byte)kvp.Value.Type, (byte)kvp.Value.Connectivity));
            }

            tiles.Sort();
            foreach ((int x, int y, byte t, byte c) in tiles)
            {
                h = (h ^ (uint)x) * 1099511628211UL;
                h = (h ^ (uint)y) * 1099511628211UL;
                h = (h ^ t) * 1099511628211UL;
                h = (h ^ c) * 1099511628211UL;
            }
        }

        return h;
    }

    // A door path starts on the tile in front of a building's entrance; every other path is a street or a lane.
    private static bool IsDoorPath(LandmarkLayout layout, in LandmarkPath path) => DoorPathOwner(layout, path) >= 0;

    // The index of the plot whose front door the path starts at, or -1 for a street.
    internal static int DoorPathOwner(LandmarkLayout layout, in LandmarkPath path)
    {
        for (int i = 0; i < layout.Plots.Length; i++)
        {
            (int fx, int fy) = VillageBlueprint.FrontTile(layout.Plots[i]);
            if (MathF.Abs(path.X0 - (fx + 0.5f)) < 0.01f && MathF.Abs(path.Y0 - (fy + 0.5f)) < 0.01f)
            {
                return i;
            }
        }

        return -1;
    }

    // Tiles reachable on foot from the square: doors open (players open them), walls, furniture and solid terrain block.
    private static HashSet<(int, int)> FloodFromSquare(ChunkWorld2D world, ProceduralWorldGenerator gen, PointOfInterest poi)
    {
        byte level = gen.SampleTile(poi.TileX, poi.TileY).HeightLevel;
        (int, int) start = (poi.TileX, poi.TileY + 2);
        HashSet<(int, int)> seen = [start];
        Queue<(int, int)> queue = new();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            (int cx, int cy) = queue.Dequeue();
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if (Math.Abs(nx - poi.TileX) > poi.Radius + 2 || Math.Abs(ny - poi.TileY) > poi.Radius + 2 || seen.Contains((nx, ny)))
                {
                    continue;
                }

                bool door = world.Buildings.TryGetStructure(nx, ny, out StructureTile s) && s.Category == StructureCategory.Door;
                MapTile terrain = gen.SampleTile(nx, ny);
                if ((world.Buildings.IsSolidAt(nx, ny) && !door) || terrain.IsSolid || terrain.HeightLevel != level)
                {
                    continue;
                }

                _ = seen.Add((nx, ny));
                queue.Enqueue((nx, ny));
            }
        }

        return seen;
    }

    // Tiles reachable on foot from (x, y): doors count as passable (players open them), solid structures and terrain block.
    private static HashSet<(int, int)> FloodWalkable(ChunkWorld2D world, int x, int y, VillagePlot plot)
    {
        HashSet<(int, int)> seen = [(x, y)];
        Queue<(int, int)> queue = new();
        queue.Enqueue((x, y));
        while (queue.Count > 0)
        {
            (int cx, int cy) = queue.Dequeue();
            foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if (Math.Abs(nx - x) > 16 || Math.Abs(ny - y) > 16 || seen.Contains((nx, ny)))
                {
                    continue;
                }

                bool door = world.Buildings.TryGetStructure(nx, ny, out StructureTile s) && s.Category == StructureCategory.Door;
                if ((world.Buildings.IsSolidAt(nx, ny) && !door) || (!plot.Contains(nx, ny) && world.PeekTileAtWorld(nx, ny).IsSolid))
                {
                    continue;
                }

                _ = seen.Add((nx, ny));
                queue.Enqueue((nx, ny));
            }
        }

        return seen;
    }
}
