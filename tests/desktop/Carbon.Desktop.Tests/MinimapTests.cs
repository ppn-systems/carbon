// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Networking.Session;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Items.Catalog;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Minimap marker placement, the per-chunk terrain cache and its invalidation, and the HUD column layout.</summary>
public sealed class MinimapTests
{
    private const ulong Seed = 1337UL;
    private const float TileSize = 32f;
    private static readonly RectangleF s_map = new(1000f, 100f, 180f, 180f);

    /// <summary>The roles the shipped NPC archetypes have (what MultiplayerSession reports).</summary>
    private static readonly CatalogNpcLookup s_npcs = new();

    [Fact]
    public void Project_ViewCentreAndEdge_LandOnMapCentreAndEdge()
    {
        MinimapView view = new(100.5f, -40.5f, 30f, s_map);

        view.Project(100.5f, -40.5f).Should().Be(s_map.Center, "the player's tile is the map centre");
        view.Project(130.5f, -40.5f).X.Should().BeApproximately(s_map.Right, 1e-3f, "radius tiles east is the right edge");
        view.Project(100.5f, -70.5f).Y.Should().BeApproximately(s_map.Y, 1e-3f, "north is up");
    }

    [Fact]
    public void Collect_ActorsInView_ArePlacedAtTheirTileAndClassified()
    {
        Vector2 player = new(100.5f * TileSize, 200.5f * TileSize);
        MinimapView view = MinimapView.Create(player, TileSize, 1, s_map);
        List<RemoteEntityView> remotes =
        [
            Actor(1, EntityKind.Monster, player + new Vector2(5f * TileSize, 0f)),
            Actor(2, EntityKind.Player, player + new Vector2(0f, -8f * TileSize)),
            Actor(3, EntityKind.Npc, player + new Vector2(-3f * TileSize, 2f * TileSize), WorldActorCatalog.MasterBlacksmith),
            Actor(4, EntityKind.Npc, player + new Vector2(-6f * TileSize, 0f), WorldActorCatalog.VillageMerchant),
            Actor(5, EntityKind.Npc, player + new Vector2(2f * TileSize, 2f * TileSize), WorldActorCatalog.VillageGuard),
            Actor(6, EntityKind.LootDrop, player + new Vector2(1f * TileSize, 0f)),
            Actor(7, EntityKind.Monster, player + new Vector2(500f * TileSize, 0f)),
        ];
        Span<MinimapIcon> icons = stackalloc MinimapIcon[16];

        int count = MinimapIconCollector.Collect(null, remotes, s_npcs, lockedTargetNetworkId: 1, in view, TileSize, icons);

        count.Should().Be(5, "loot and the far monster are not on the map");
        float ppt = view.PixelsPerTile;
        icons[0].Should().Be(new MinimapIcon(MinimapIconKind.Monster, s_map.Center + new Vector2(5f * ppt, 0f), IsLockedTarget: true));
        icons[1].Should().Be(new MinimapIcon(MinimapIconKind.Player, s_map.Center + new Vector2(0f, -8f * ppt)));
        icons[2].Kind.Should().Be(MinimapIconKind.Blacksmith);
        icons[2].Position.Should().Be(s_map.Center + new Vector2(-3f * ppt, 2f * ppt));
        icons[3].Kind.Should().Be(MinimapIconKind.Trader);
        icons[4].Kind.Should().Be(MinimapIconKind.Npc);
    }

    [Fact]
    public async Task Collect_NpcRolesFromTheSession_MarkTradersAndBlacksmiths()
    {
        // The minimap reads NPC roles through the session (INpcCapabilityLookup), like the trade and forge prompts.
        MultiplayerSession session = new(new DesktopOptions());
        try
        {
            Vector2 player = new(0.5f * TileSize, 0.5f * TileSize);
            MinimapView view = MinimapView.Create(player, TileSize, 1, s_map);
            List<RemoteEntityView> remotes =
            [
                Actor(1, EntityKind.Npc, player + new Vector2(TileSize, 0f), WorldActorCatalog.MasterBlacksmith),
                Actor(2, EntityKind.Npc, player + new Vector2(-TileSize, 0f), WorldActorCatalog.GeneralTrader),
                Actor(3, EntityKind.Npc, player + new Vector2(0f, TileSize), WorldActorCatalog.ShrineKeeper),
            ];
            MinimapIcon[] icons = new MinimapIcon[4];

            MinimapIconCollector.Collect(null, remotes, session, 0, in view, TileSize, icons).Should().Be(3);

            icons[0].Kind.Should().Be(MinimapIconKind.Blacksmith);
            icons[1].Kind.Should().Be(MinimapIconKind.Trader);
            icons[2].Kind.Should().Be(MinimapIconKind.Npc);
            session.AuthoritativeIsNight.Should().BeNull("offline there is no server clock, so the HUD clock hides");
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(true);
        }
    }

    [Fact]
    public void Collect_ActorJustOutsideTheMapEdge_IsNotClampedOntoTheMap()
    {
        Vector2 player = new(0.5f * TileSize, 0.5f * TileSize);
        MinimapView view = MinimapView.Create(player, TileSize, 0, s_map);
        List<RemoteEntityView> remotes = [Actor(1, EntityKind.Monster, player + new Vector2((view.RadiusTiles + 1f) * TileSize, 0f))];
        Span<MinimapIcon> icons = stackalloc MinimapIcon[4];

        MinimapIconCollector.Collect(null, remotes, s_npcs, 0, in view, TileSize, icons).Should().Be(0, "only what fits the map is drawn, never pinned to the edge");
    }

    [Fact]
    public void Collect_StreamedVillage_GetsOneMarkerAtItsCentre()
    {
        ProceduralWorldGenerator generator = new(Seed);
        ChunkWorld2D world = new(generator);
        PointOfInterest village = FindVillage(generator);
        Vector2 nearby = new((village.TileX + 6.5f) * TileSize, (village.TileY + 4.5f) * TileSize);
        world.UpdateAroundPosition(nearby, chunkRadius: 3, evictionRadius: 400);
        MinimapView view = MinimapView.Create(nearby, TileSize, 1, s_map);
        Span<MinimapIcon> icons = stackalloc MinimapIcon[32];

        int count = MinimapIconCollector.Collect(world, [], s_npcs, 0, in view, TileSize, icons);

        icons[..count].ToArray().Should().ContainSingle(i => i.Kind == MinimapIconKind.Village, "a village overlapping several chunks is still one marker")
            .Which.Position.Should().Be(view.Project(village.TileX + 0.5f, village.TileY + 0.5f));
    }

    [Fact]
    public void Prepare_VisibleChunks_BakesEachOnceAndOnlyDirtyOnesAgain()
    {
        ChunkWorld2D world = StreamedWorld(out (int X, int Y) centre);
        RecordingSink sink = new();
        MinimapTerrainCache cache = new();
        (int, int, int, int) range = (centre.X - 1, centre.Y - 1, centre.X + 1, centre.Y + 1);

        cache.Prepare(world, null, range, sink, maxBakes: 100).Should().Be(9);
        cache.Prepare(world, null, range, sink, maxBakes: 100).Should().Be(0, "nothing changed, so nothing is re-baked");

        cache.Invalidate(centre.X, centre.Y);
        cache.Invalidate(centre.X + 50, centre.Y); // not cached: nothing to re-bake
        cache.IsDirty(centre.X, centre.Y).Should().BeTrue();
        cache.TryGetSlot(centre.X, centre.Y, out _, out _).Should().BeTrue("the stale pixels stay on screen until the re-bake");
        sink.Uploads.Clear();

        cache.Prepare(world, null, range, sink, maxBakes: 100).Should().Be(1, "only the dirty chunk is re-baked");
        sink.Uploads.Should().Equal([(centre.X & 7, centre.Y & 7)]);
        cache.IsDirty(centre.X, centre.Y).Should().BeFalse();
    }

    [Fact]
    public void Prepare_BakeBudgetAndUnstreamedChunks_AreRespected()
    {
        ChunkWorld2D world = StreamedWorld(out (int X, int Y) centre);
        RecordingSink sink = new();
        MinimapTerrainCache cache = new();

        cache.Prepare(world, null, (centre.X - 1, centre.Y - 1, centre.X + 1, centre.Y + 1), sink, maxBakes: 2).Should().Be(2, "a frame bakes at most its budget");
        cache.Prepare(world, null, (centre.X + 40, centre.Y, centre.X + 41, centre.Y), sink, maxBakes: 100)
            .Should().Be(0, "chunks the client has not streamed are never generated for the minimap");
        cache.TryGetSlot(centre.X + 40, centre.Y, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Prepare_SlotReusedByAnotherChunk_BakesTheNewChunk()
    {
        ChunkWorld2D world = StreamedWorld(out (int X, int Y) centre, radius: 5);
        RecordingSink sink = new();
        MinimapTerrainCache cache = new();
        (int, int, int, int) first = (centre.X - 4, centre.Y, centre.X - 4, centre.Y);
        (int, int, int, int) wrapped = (centre.X + 4, centre.Y, centre.X + 4, centre.Y); // 8 chunks east: same ring slot

        cache.Prepare(world, null, first, sink, 10).Should().Be(1);
        cache.Prepare(world, null, wrapped, sink, 10).Should().Be(1, "the slot now belongs to another chunk");
        cache.TryGetSlot(centre.X - 4, centre.Y, out _, out _).Should().BeFalse("the evicted chunk is no longer drawn from the slot");
    }

    [Fact]
    public void Prepare_BakedPixels_UseThePaletteAndRoads()
    {
        ChunkWorld2D world = StreamedWorld(out (int X, int Y) centre);
        RecordingSink sink = new();
        new MinimapTerrainCache().Prepare(world, null, (centre.X, centre.Y, centre.X, centre.Y), sink, 1).Should().Be(1);
        world.TryGetChunk(centre.X, centre.Y, out Chunk2D? chunk).Should().BeTrue();

        for (int ly = 1; ly < Chunk2D.Size; ly += 7)
        {
            for (int lx = 0; lx < Chunk2D.Size; lx += 5)
            {
                MapTile tile = chunk!.GetTile(lx, ly);
                ColorRgba expected = MinimapPalette.GetTileColor(in tile, chunk.GetTile(lx, ly - 1).HeightLevel, chunk.IsRoad(lx, ly), chunk.IsBridge(lx, ly), null);
                int o = ((ly * Chunk2D.Size) + lx) * 4;
                new ColorRgba(sink.Last[o], sink.Last[o + 1], sink.Last[o + 2], sink.Last[o + 3]).Should().Be(expected, $"tile ({lx},{ly})");
            }
        }
    }

    [Fact]
    public void PrepareAndCollect_SteadyState_AllocateNothing()
    {
        // Next to a village, so the point-of-interest loop has work to do.
        ProceduralWorldGenerator generator = new(Seed);
        ChunkWorld2D world = new(generator);
        PointOfInterest village = FindVillage(generator);
        Vector2 player = new((village.TileX + 6.5f) * TileSize, (village.TileY + 4.5f) * TileSize);
        world.UpdateAroundPosition(player, chunkRadius: 3, evictionRadius: 400);
        int centreX = Chunk2D.ToChunkCoord(village.TileX);
        int centreY = Chunk2D.ToChunkCoord(village.TileY);
        RecordingSink sink = new() { Record = false };
        MinimapTerrainCache cache = new();
        MinimapView view = MinimapView.Create(player, TileSize, 1, s_map);
        List<RemoteEntityView> remotes = [Actor(1, EntityKind.Monster, player + new Vector2(TileSize, 0f))];
        MinimapIcon[] icons = new MinimapIcon[64];
        _ = cache.Prepare(world, null, view.VisibleChunks(), sink, 100);
        MinimapIconCollector.Collect(world, remotes, s_npcs, 0, in view, TileSize, icons).Should().BeGreaterThan(1, "the village and the monster are on the map");

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++)
        {
            cache.Invalidate(centreX, centreY);
            _ = cache.Prepare(world, null, view.VisibleChunks(), sink, 3);
            _ = MinimapIconCollector.Collect(world, remotes, s_npcs, 0, in view, TileSize, icons);
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0, "the minimap runs every frame (AGENTS.md rule 4)");
    }

    [Fact]
    public void BiomeColors_FromTheArtLibrary_AreDistinctPerBiome()
    {
        CarbonVectorArtLibrary art = CarbonVectorArtLibrary.LoadOrDefault() ?? throw new InvalidOperationException("resources/art must be available");
        BiomeType[] biomes = Enum.GetValues<BiomeType>();
        for (int i = 0; i < biomes.Length; i++)
        {
            for (int j = i + 1; j < biomes.Length; j++)
            {
                ColorRgba a = MinimapPalette.GetBiomeColor(biomes[i], art);
                ColorRgba b = MinimapPalette.GetBiomeColor(biomes[j], art);
                int distance = Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
                distance.Should().BeGreaterThan(36, $"{biomes[i]} and {biomes[j]} must read differently on the minimap");
            }
        }
    }

    [Fact]
    public void GetArrow_Facing_PointsTheTipThatWay()
    {
        Vector2 c = new(50f, 50f);
        (Vector2 east, _, _) = MinimapRadarWidget.GetArrow(c, new Vector2(3f, 0f));
        (Vector2 north, _, _) = MinimapRadarWidget.GetArrow(c, new Vector2(0f, -1f));
        (Vector2 unknown, _, _) = MinimapRadarWidget.GetArrow(c, Vector2.Zero);

        east.Should().Be(new Vector2(57f, 50f));
        north.Should().Be(new Vector2(50f, 43f));
        unknown.Y.Should().BeGreaterThan(c.Y, "no facing yet: the arrow points south like the idle character");
    }

    [Theory]
    [InlineData(-93, -39, "(-93, -39)")]
    [InlineData(0, 12345, "(0, 12345)")]
    public void FormatCoordinates_AnyTile_MatchesInvariantText(int x, int y, string expected)
    {
        Span<char> buffer = stackalloc char[32];
        new string(MinimapRadarWidget.FormatCoordinates(x, y, buffer)).Should().Be(expected);
    }

    [Fact]
    public void HudComposition_MainHud_DrawsTheClockAndMinimap() =>
        HudComposition.MainHud.ToArray().Should().Contain([HudElement.Clock, HudElement.Minimap]);

    private static RemoteEntityView Actor(ulong id, EntityKind kind, Vector2 position, ushort archetype = 0) =>
        new(id, kind, string.Empty, position, Vector2.Zero, 8f, 10f, 10f, ArchetypeId: archetype);

    private static ChunkWorld2D StreamedWorld(out (int X, int Y) centreChunk, int radius = 2)
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int tx, int ty) = world.SpawnTile;
        world.UpdateAroundPosition(new Vector2((tx + 0.5f) * world.TileSize, (ty + 0.5f) * world.TileSize), chunkRadius: radius, evictionRadius: 400);
        centreChunk = (Chunk2D.ToChunkCoord(tx), Chunk2D.ToChunkCoord(ty));
        return world;
    }

    private static PointOfInterest FindVillage(ProceduralWorldGenerator generator)
    {
        for (int r = 0; r < 12; r++)
        {
            for (int sy = -r; sy <= r; sy++)
            {
                for (int sx = -r; sx <= r; sx++)
                {
                    PointOfInterest poi = generator.Landmarks.GetSite(sx, sy);
                    if (poi.Type is PointOfInterestType.RiverVillage or PointOfInterestType.DesertVillage or PointOfInterestType.SnowVillage or PointOfInterestType.SwampVillage)
                    {
                        return poi;
                    }
                }
            }
        }

        throw new InvalidOperationException("seed 1337 has a village within 12 sites of the origin");
    }

    private sealed class CatalogNpcLookup : INpcCapabilityLookup
    {
        public NpcCapabilities GetNpcCapabilities(ushort archetypeId) => WorldActorCatalog.GetNpcSpec(archetypeId).Capabilities;
    }

    private sealed class RecordingSink : IMinimapTextureSink
    {
        public List<(int X, int Y)> Uploads { get; } = [];

        public byte[] Last { get; } = new byte[Chunk2D.Size * Chunk2D.Size * 4];

        public bool Record { get; init; } = true;

        public void UploadChunk(int slotX, int slotY, ReadOnlySpan<byte> rgba)
        {
            if (this.Record)
            {
                this.Uploads.Add((slotX, slotY));
            }

            rgba.CopyTo(this.Last);
        }
    }
}
