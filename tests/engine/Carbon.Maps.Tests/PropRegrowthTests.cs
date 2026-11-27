// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Generators.Stages;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// ADR-040: harvested flora comes back after its <c>props.json</c> delay, stone and ore never do, starter forage in the
/// spawn clearing comes back fast, a pending regrowth survives a restart, and the respawn scan does not allocate.
/// </summary>
public sealed class PropRegrowthTests
{
    private const ulong Seed = 42UL;

    /// <summary>A valid <c>trees</c> block for documents built in tests.</summary>
    internal const string TreesJson = """
        "trees": {
          "deferWithinTiles": 0,
          "stump": { "seconds": 30, "durability": 30, "yield": 1, "saplingChance": 0.5 },
          "sapling": { "seconds": 20, "durability": 10, "yield": 0 },
          "young": { "seconds": 10, "durabilityShare": 0.5, "yield": 2 },
          "mature": { "yield": 3, "saplingChance": 0.2 },
          "dig": { "bareHandDamage": 4, "axeEfficiency": 0.5 }
        }
        """;

    [Fact]
    public void LoadEmbedded_EveryStoneAndOreType_NeverComesBack()
    {
        PropRegrowthRules rules = PropRegrowthRules.LoadEmbedded();
        foreach (MapPropType type in Enum.GetValues<MapPropType>())
        {
            if (MapProp.CategoryOf(type) != PropCategory.Mineral)
            {
                continue;
            }

            bool starterForage = SpawnClearingStage.IsStarterForage(type);
            Assert.Equal(0UL, rules.DelayTicks(type, inSpawnClearing: false, tickRate: 30));
            Assert.Equal(starterForage ? (ulong)(rules.SpawnForageRespawnSeconds * 30f) : 0UL, rules.DelayTicks(type, inSpawnClearing: true, tickRate: 30));
        }
    }

    [Fact]
    public void LoadFromJson_ListsAnOre_IsRejected()
    {
        const string Json = """{ "spawnForage": { "respawnSeconds": 45 }, "blockedRetrySeconds": 10, "regrowSeconds": { "ironVein": 60 } }""";

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => PropRegrowthRules.LoadFromJson(Json));

        Assert.Contains("ironVein", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "spawnForage": { "respawnSeconds": 45 }, "blockedRetrySeconds": 10, "regrowSeconds": { "notAProp": 60 } }""")]
    [InlineData("""{ "spawnForage": { "respawnSeconds": 45 }, "blockedRetrySeconds": 10, "regrowSeconds": { "1": 60 } }""")]
    [InlineData("""{ "spawnForage": { "respawnSeconds": 45 }, "blockedRetrySeconds": 10, "regrowSeconds": { "bush": 0 } }""")]
    [InlineData("""{ "spawnForage": { "respawnSeconds": 0 }, "blockedRetrySeconds": 10, "regrowSeconds": {} }""")]
    [InlineData("""{ "spawnForage": { "respawnSeconds": 45 }, "regrowSeconds": {} }""")]
    public void LoadFromJson_BadDocument_IsRejected(string json) =>
        Assert.Throws<InvalidDataException>(() => PropRegrowthRules.LoadFromJson(json));

    [Fact]
    public void DelaySeconds_StarterForage_IsFastOnlyInsideTheSpawnClearing()
    {
        PropRegrowthRules rules = PropRegrowthRules.LoadFromJson(
            """{ "spawnForage": { "respawnSeconds": 40 }, "blockedRetrySeconds": 10, "regrowSeconds": { "dryBush": 600, "deadTree": 2700 }, """ + TreesJson + "}");

        Assert.Equal(40f, rules.DelaySeconds(MapPropType.DryBush, inSpawnClearing: true));
        Assert.Equal(600f, rules.DelaySeconds(MapPropType.DryBush, inSpawnClearing: false));
        Assert.Equal(40f, rules.DelaySeconds(MapPropType.LooseStones, inSpawnClearing: true));
        Assert.Equal(0f, rules.DelaySeconds(MapPropType.LooseStones, inSpawnClearing: false));
        Assert.Equal(2700f, rules.DelaySeconds(MapPropType.DeadTree, inSpawnClearing: true));
        Assert.Equal(1200UL, rules.DelayTicks(MapPropType.LooseStones, inSpawnClearing: true, tickRate: 30));
    }

    [Fact]
    public void IsStarterForage_EveryPropTheClearingPlaces_IsStarterForage()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int sx, int sy) = world.SpawnTile;
        int found = 0;
        for (int dy = -SpawnClearingStage.Radius; dy <= SpawnClearingStage.Radius; dy++)
        {
            for (int dx = -SpawnClearingStage.Radius; dx <= SpawnClearingStage.Radius; dx++)
            {
                if (SpawnClearingStage.IsInClearing((sx, sy), sx + dx, sy + dy) && world.TryGetPropAt(sx + dx, sy + dy, out MapProp prop))
                {
                    Assert.True(SpawnClearingStage.IsStarterForage(prop.Type), $"{prop.Type} at ({dx},{dy}) is starter forage");
                    found++;
                }
            }
        }

        Assert.True(found >= 6, "the clearing holds the starter forage");
        Assert.False(SpawnClearingStage.IsInClearing((sx, sy), sx + SpawnClearingStage.Radius + 1, sy));
    }

    [Fact]
    public void ProcessPropRespawns_StoneDestroyedWithoutATimer_NeverComesBack()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y) = FindProp(world, p => p.Category == PropCategory.Mineral);

        Assert.True(world.TryDamagePropAt(x, y, float.MaxValue, byte.MaxValue, respawnAtTick: 0UL, out MapProp destroyed));
        Assert.True(destroyed.Destructibility.IsDestroyed);

        Assert.Equal(0, world.ProcessPropRespawns(ulong.MaxValue));
        Assert.True(world.TryGetPropAt(x, y, out MapProp after));
        Assert.True(after.Destructibility.IsDestroyed, "stone and ore are permanent");
    }

    [Fact]
    public void ProcessPropRespawns_TreeDue_StandsAgainAtFullDurabilityInPlace()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y) = FindProp(world, p => MapProp.IsTree(p.Type));
        Assert.True(world.TryGetPropAt(x, y, out MapProp standing));
        Assert.True(world.TryDamagePropAt(x, y, float.MaxValue, byte.MaxValue, respawnAtTick: 600UL, out _));
        List<(int TileX, int TileY, uint Revision, float Durability, TreeGrowthStage Stage)> respawned = [];

        Assert.Equal(0, world.ProcessPropRespawns(599UL, null, 30, null, null, respawned));
        Assert.Equal(1, world.ProcessPropRespawns(600UL, null, 30, null, null, respawned));

        Assert.True(world.TryGetPropAt(x, y, out MapProp regrown));
        Assert.Equal(standing.Destructibility, regrown.Destructibility);
        Assert.Equal((x, y), (respawned[0].TileX, respawned[0].TileY));
        Assert.Equal(standing.Destructibility.MaxDurability, respawned[0].Durability);
    }

    [Fact]
    public void SerializeDeserialize_PendingRegrowthAcrossARestart_ComesBackAfterTheTimeThatWasLeft()
    {
        // Before the restart: felled at tick 1000, due at 1900; saved at tick 1300 (600 ticks left).
        ChunkWorld2D before = new(new ProceduralWorldGenerator(Seed));
        (int x, int y) = FindProp(before, p => MapProp.IsTree(p.Type));
        Assert.True(before.TryDamagePropAt(x, y, float.MaxValue, byte.MaxValue, respawnAtTick: 1900UL, out _));
        Assert.True(before.TryGetDelta(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y), out ChunkDelta? saved));
        byte[] row = saved!.Serialize(currentTick: 1300UL);

        // After the restart the world tick starts again at 0.
        ChunkWorld2D after = new(new ProceduralWorldGenerator(Seed));
        after.ImportDelta(ChunkDelta.Deserialize(row, currentTick: 0UL));
        Assert.True(after.TryGetPropAt(x, y, out MapProp felled));
        Assert.True(felled.Destructibility.IsDestroyed, "the felled tree is still down after the restart");

        Assert.Equal(0, after.ProcessPropRespawns(599UL));
        Assert.Equal(1, after.ProcessPropRespawns(600UL));
        Assert.True(after.TryGetPropAt(x, y, out MapProp regrown));
        Assert.False(regrown.Destructibility.IsDestroyed, "the regrowth timer survived the restart");
    }

    [Fact]
    public void Serialize_RespawnAlreadyDue_StaysPendingOnReload()
    {
        ChunkDelta delta = new(0, 0);
        delta.SetPropState(3, 4, PropDestructibility.Create(50f) with { CurrentDurability = 0f, IsDestroyed = true }, respawnAtTick: 100UL);

        ChunkDelta reloaded = ChunkDelta.Deserialize(delta.Serialize(currentTick: 500UL), currentTick: 0UL);

        Assert.True(reloaded.TryGetRespawnTick(3, 4, out ulong due));
        Assert.Equal(1UL, due);
    }

    [Fact]
    public void ScheduleLegacyRespawns_RowFromFormat3_ReschedulesFloraAndKeepsStoneGone()
    {
        ChunkWorld2D source = new(new ProceduralWorldGenerator(Seed));
        ((int tx, int ty), (int sx, int sy)) = FindTreeAndStoneInOneChunk(source);
        Assert.True(source.TryDamagePropAt(tx, ty, float.MaxValue, out _));
        Assert.True(source.TryDamagePropAt(sx, sy, float.MaxValue, out _));
        Assert.True(source.TryGetDelta(Chunk2D.ToChunkCoord(tx), Chunk2D.ToChunkCoord(ty), out ChunkDelta? delta));
        byte[] legacyRow = ToFormat3(delta!.Serialize(currentTick: 0UL));

        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        world.ImportDelta(ChunkDelta.Deserialize(legacyRow, currentTick: 0UL));
        List<long> resave = [];
        int scheduled = world.ScheduleLegacyRespawns(
            (type, _, _) => MapProp.CategoryOf(type) == PropCategory.Flora ? 300UL : 0UL,
            currentTick: 50UL,
            resave);

        Assert.Equal(1, scheduled);
        Assert.Equal([Chunk2D.PackKey(Chunk2D.ToChunkCoord(tx), Chunk2D.ToChunkCoord(ty))], resave);
        Assert.Equal(1, world.ProcessPropRespawns(350UL));
        Assert.True(world.TryGetPropAt(tx, ty, out MapProp tree));
        Assert.False(tree.Destructibility.IsDestroyed, "a tree felled before format 4 regrows");
        Assert.True(world.TryGetPropAt(sx, sy, out MapProp stone));
        Assert.True(stone.Destructibility.IsDestroyed, "stone mined before format 4 stays gone");
        Assert.Equal(0, world.ScheduleLegacyRespawns((_, _, _) => 1UL, 50UL, resave));
    }

    [Fact]
    public void ProcessPropRespawns_TileBlocked_WaitsAndGrowsOnceItIsFree()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y) = FindProp(world, p => MapProp.IsTree(p.Type));
        Assert.True(world.TryDamagePropAt(x, y, float.MaxValue, byte.MaxValue, respawnAtTick: 100UL, out _));
        bool playerOnTile = true;
        bool IsBlocked(int tileX, int tileY) => playerOnTile && tileX == x && tileY == y;

        PropRegrowthRules rules = PropRegrowthRules.LoadFromJson(
            """{ "spawnForage": { "respawnSeconds": 45 }, "blockedRetrySeconds": 5, "regrowSeconds": {}, """ + TreesJson + "}");

        // 5 s at 10 ticks per second: a blocked prop tries again 50 ticks later.
        Assert.Equal(0, world.ProcessPropRespawns(100UL, rules, 10, IsBlocked, null, respawnedProps: null));
        playerOnTile = false;
        Assert.Equal(0, world.ProcessPropRespawns(149UL, rules, 10, IsBlocked, null, respawnedProps: null));
        Assert.Equal(1, world.ProcessPropRespawns(150UL, rules, 10, IsBlocked, null, respawnedProps: null));
    }

    [Fact]
    public void ProcessPropRespawns_StructureOnTheStumpTile_NeverGrowsIntoIt()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y) = FindProp(world, p => MapProp.IsTree(p.Type));
        Assert.True(world.TryDamagePropAt(x, y, float.MaxValue, byte.MaxValue, respawnAtTick: 100UL, out _));
        Assert.Equal(
            PlacementValidationResult.Success,
            world.Buildings.TryPlaceStructure(x, y, StructureType.WoodFloor, BiomeType.LushPlains, hasNaturalSolidProp: false, out _));

        Assert.Equal(0, world.ProcessPropRespawns(10_000UL));
        Assert.True(world.TryGetPropAt(x, y, out MapProp stump));
        Assert.True(stump.Destructibility.IsDestroyed);
    }

    [Fact]
    public void ProcessPropRespawns_PendingAndDueRespawns_AllocatesNothing()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        List<(int X, int Y)> trees = FindProps(world, p => MapProp.IsTree(p.Type), count: 8);
        List<(int TileX, int TileY, uint Revision, float Durability, TreeGrowthStage Stage)> respawned = new(16);
        static bool NeverBlocked(int tileX, int tileY) => false;
        Func<int, int, bool> blocked = NeverBlocked;

        // Warm-up: one cycle grows the scratch lists to capacity.
        Fell(world, trees, respawnAt: 10UL);
        Assert.Equal(trees.Count, world.ProcessPropRespawns(10UL, null, 30, blocked, null, respawned));

        Fell(world, trees, respawnAt: 20UL);
        respawned.Clear();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int pending = world.ProcessPropRespawns(19UL, null, 30, blocked, null, respawned);
        int due = world.ProcessPropRespawns(20UL, null, 30, blocked, null, respawned);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, pending);
        Assert.Equal(trees.Count, due);
        Assert.Equal(0L, allocated);
    }

    private static void Fell(ChunkWorld2D world, List<(int X, int Y)> tiles, ulong respawnAt)
    {
        foreach ((int x, int y) in tiles)
        {
            Assert.True(world.TryDamagePropAt(x, y, float.MaxValue, byte.MaxValue, respawnAt, out _));
        }
    }

    private static ((int X, int Y) Tree, (int X, int Y) Stone) FindTreeAndStoneInOneChunk(ChunkWorld2D world)
    {
        for (int cy = -4; cy <= 4; cy++)
        {
            for (int cx = -4; cx <= 4; cx++)
            {
                (int X, int Y)? tree = null;
                (int X, int Y)? stone = null;
                foreach (MapProp prop in world.GetOrCreateChunk(cx, cy).Props)
                {
                    (int X, int Y) tile = ((cx * Chunk2D.Size) + prop.LocalTileX, (cy * Chunk2D.Size) + prop.LocalTileY);
                    if (MapProp.IsTree(prop.Type))
                    {
                        tree ??= tile;
                    }
                    else if (prop.Category == PropCategory.Mineral && prop.Destructibility.IsDestructible && !SpawnClearingStage.IsStarterForage(prop.Type))
                    {
                        stone ??= tile;
                    }
                }

                if (tree is { } t && stone is { } s)
                {
                    return (t, s);
                }
            }
        }

        throw new InvalidOperationException("no chunk near the origin holds both a tree and stone");
    }

    private static (int X, int Y) FindProp(ChunkWorld2D world, Func<MapProp, bool> match) => FindProps(world, match, count: 1)[0];

    private static List<(int X, int Y)> FindProps(ChunkWorld2D world, Func<MapProp, bool> match, int count)
    {
        List<(int X, int Y)> found = [];
        for (int cy = -3; cy <= 3; cy++)
        {
            for (int cx = -3; cx <= 3; cx++)
            {
                Chunk2D chunk = world.GetOrCreateChunk(cx, cy);
                foreach (MapProp prop in chunk.Props)
                {
                    if (prop.Destructibility.IsDestructible && !prop.Destructibility.IsDestroyed && match(prop))
                    {
                        found.Add(((cx * Chunk2D.Size) + prop.LocalTileX, (cy * Chunk2D.Size) + prop.LocalTileY));
                        if (found.Count == count)
                        {
                            return found;
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("the seeded world has no such prop near the origin");
    }

    /// <summary>Rewrites a current (format 5) row as format 3 (no respawn timers), what a server saved before ADR-040.</summary>
    private static byte[] ToFormat3(byte[] row)
    {
        const int Header = 15;
        const int TileSize = 18;
        const int PropSize = 18;
        int tiles = BinaryPrimitives.ReadUInt16LittleEndian(row.AsSpan(Header));
        int propsAt = Header + 2 + (tiles * TileSize);
        int props = BinaryPrimitives.ReadUInt16LittleEndian(row.AsSpan(propsAt));
        List<byte> legacy = [.. row.AsSpan(0, propsAt + 2)];
        legacy[0] = 3;
        for (int i = 0; i < props; i++)
        {
            legacy.AddRange(row.AsSpan(propsAt + 2 + (i * PropSize), 12));
        }

        legacy.AddRange(row.AsSpan(propsAt + 2 + (props * PropSize)));
        return [.. legacy];
    }
}
