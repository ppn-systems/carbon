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
using Xunit.Abstractions;

namespace Carbon.Maps.Tests;

public sealed class VillageGenerationTests
{
    // Pins every village plot (template, origin, turns) of seed 42 over a 13x13 site window (generator version 16:
    // villages built from real structures, ADR-025). Update it only together with GeneratorVersion.
    private const ulong GoldenVillagePlotFingerprint = 15994986282064531618UL;

    private readonly ITestOutputHelper _output;

    public VillageGenerationTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Catalog_Embedded_ParsesEveryStyleAndRole()
    {
        VillageBuildingCatalog catalog = VillageBuildingCatalog.Default;
        foreach (VillageStyle style in Enum.GetValues<VillageStyle>())
        {
            foreach (VillageBuildingRole role in Enum.GetValues<VillageBuildingRole>())
            {
                Assert.True(catalog.Pick(role, style, 7U) >= 0, $"No {role} template for style {style}.");
            }
        }
    }

    [Fact]
    public void Catalog_Parse_RejectsUnknownCellsAndTypes()
    {
        const string Styles = """
            "styles": {
              "temperate": { "wall": "WoodWall", "door": "WoodDoor", "floor": "WoodFloor" },
              "desert": { "wall": "WoodWall", "door": "WoodDoor", "floor": "WoodFloor" },
              "snow": { "wall": "WoodWall", "door": "WoodDoor", "floor": "WoodFloor" },
              "swamp": { "wall": "WoodWall", "door": "WoodDoor", "floor": "WoodFloor" }
            }
            """;
        Assert.Throws<FormatException>(() => VillageBuildingCatalog.Parse("{" + Styles + """, "buildings": [ { "id": "a", "role": "house", "grid": [ "WXW" ] } ] }"""));
        Assert.Throws<FormatException>(() => VillageBuildingCatalog.Parse("{" + Styles + """, "buildings": [ { "id": "a", "role": "house", "grid": [ "W.W" ], "furniture": [ { "type": "Nope", "at": [1, 0] } ] } ] }"""));
        Assert.Throws<FormatException>(() => VillageBuildingCatalog.Parse("{" + Styles + """, "buildings": [ { "id": "a", "role": "house", "grid": [ "W.W" ], "furniture": [ { "type": "WoodChest", "at": [0, 0] } ] } ] }"""));
        Assert.Throws<FormatException>(() => VillageBuildingCatalog.Parse("""{ "styles": { "temperate": { "wall": "WoodFloor", "door": "WoodDoor", "floor": "WoodFloor" } }, "buildings": [] }"""));
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
            bool stall = false, smithy = false, store = false;
            int houses = 0;
            foreach (VillagePlot plot in layout.Plots)
            {
                stall |= plot.Role == VillageBuildingRole.Stall;
                smithy |= plot.Role == VillageBuildingRole.Smithy;
                store |= plot.Role == VillageBuildingRole.Store;
                houses += plot.Role == VillageBuildingRole.House ? 1 : 0;
            }

            if (stall && smithy && store && houses >= 2)
            {
                complete++;
            }

            Assert.True(layout.Plots.Length >= 3, $"{poi.Type} at {poi.TileX},{poi.TileY} has only {layout.Plots.Length} buildings.");
        }

        _output.WriteLine($"{villages} villages, {complete} complete; {string.Join(", ", byType)}");
        Assert.True(villages >= 8, $"Only {villages} villages found.");
        Assert.True(complete * 10 >= villages * 9, $"Only {complete} of {villages} villages have stall, smithy, store and 2+ houses.");
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(2UL)]
    public void Plan_Plots_StayInsideTheFootprintAndApart(ulong seed)
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
                    bool apart = a.OriginX + a.Width < b.OriginX || b.OriginX + b.Width < a.OriginX ||
                        a.OriginY + a.Height < b.OriginY || b.OriginY + b.Height < a.OriginY;
                    Assert.True(apart, $"Plots {i} and {j} of the village at {poi.TileX},{poi.TileY} touch.");
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
                VillageBuildingTemplate template = VillageBuildingCatalog.Default.Templates[plot.TemplateIndex];
                (int fx, int fy) = VillageBlueprint.FrontTile(VillageBuildingCatalog.Default, plot);
                HashSet<(int, int)> reach = FloodWalkable(world, fx, fy, plot);
                for (int y = 0; y < template.Height; y++)
                {
                    for (int x = 0; x < template.Width; x++)
                    {
                        if (template.CellAt(x, y) != VillageBuildingTemplate.FloorCell)
                        {
                            continue;
                        }

                        (int wx, int wy) = plot.ToWorld(template, x, y);
                        Assert.True(
                            RoomDetector.IsTileEnclosed(wx, wy, world.Buildings.IsRoomBoundaryAt),
                            $"{template.Id} cell {x},{y} at {wx},{wy} is not enclosed.");
                        if (!world.Buildings.IsSolidAt(wx, wy))
                        {
                            Assert.True(reach.Contains((wx, wy)), $"{template.Id} cell {x},{y} cannot be reached from its door.");
                        }
                    }
                }

                if (VillageBlueprint.TryGetNpcTile(VillageBuildingCatalog.Default, plot, out int nx, out int ny))
                {
                    Assert.False(world.Buildings.IsSolidAt(nx, ny), $"{template.Id} NPC spot is blocked.");
                    Assert.True(reach.Contains((nx, ny)), $"{template.Id} NPC spot cannot be reached from the entrance.");
                }

                checkedBuildings++;
            }
        }

        Assert.True(checkedBuildings > 20);
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
        VillageBuildingTemplate template = VillageBuildingCatalog.Default.Templates[plot.TemplateIndex];
        (int wallX, int wallY) = plot.ToWorld(template, 0, 0);
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
                (int fx, int fy) = plot.ToWorld(template, x, y);
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
                    Mix(p.TemplateIndex);
                    Mix((ulong)p.Style);
                    Mix((uint)p.OriginX);
                    Mix((uint)p.OriginY);
                    Mix(p.Turns);
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
