// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Carbon.Contracts.Maps.S2C;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Networking;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Testing;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// ADR-040 D1: a felled tree is a stump that grows back through sapling and young tree to mature on the <c>props.json</c>
/// stage times, also while its chunk is not loaded and across a restart; a dug-up stump never comes back.
/// </summary>
public sealed class TreeGrowthTests
{
    private const ulong Seed = 42UL;
    private const int TickRate = 10;

    private static readonly PropRegrowthRules s_rules = PropRegrowthRules.LoadFromJson(
        """{ "spawnForage": { "respawnSeconds": 45 }, "blockedRetrySeconds": 5, "regrowSeconds": {}, """ + PropRegrowthTests.TreesJson + "}");

    [Fact]
    public void LoadEmbedded_Trees_GrowBackInAboutFortyFiveMinutesAndAreNotInRegrowSeconds()
    {
        PropRegrowthRules rules = PropRegrowthRules.LoadEmbedded();
        TreeGrowthRules trees = rules.Trees;

        Assert.Equal(2700f, trees.Stump.Seconds + trees.Sapling.Seconds + trees.Young.Seconds);
        Assert.True(trees.Young.Yield < trees.MatureYield, "a young tree gives fewer logs");
        foreach (MapPropType type in Enum.GetValues<MapPropType>())
        {
            Assert.False(MapProp.IsTree(type) && rules.RegrowSeconds.ContainsKey(type), $"{type} grows through stages, not whole");
        }
    }

    [Fact]
    public void LoadEmbedded_SaplingDrops_AreRareButSustainable()
    {
        // ADR-040 D1c (owner): at most one sapling, 3 % per felled mature tree and 1 % per dug-up stump. The world is
        // infinite, so wild trees are plentiful; saplings stay rare so farming whole forests is not a strategy.
        TreeGrowthRules trees = PropRegrowthRules.LoadEmbedded().Trees;

        Assert.Equal(0.03f, trees.MatureSaplingChance);
        Assert.Equal(0.01f, trees.StumpSaplingChance);
    }

    [Fact]
    public void LoadFromJson_TreeInRegrowSeconds_IsRejected()
    {
        string json = """{ "spawnForage": { "respawnSeconds": 45 }, "blockedRetrySeconds": 5, "regrowSeconds": { "oakTree": 60 }, """ + PropRegrowthTests.TreesJson + "}";

        InvalidDataException error = Assert.Throws<InvalidDataException>(() => PropRegrowthRules.LoadFromJson(json));

        Assert.Contains("oakTree", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessPropRespawns_Stump_GrowsSaplingYoungAndMatureOnTheStageTimes()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, MapProp mature) = FindTree(world);
        TreeGrowthRules trees = s_rules.Trees;
        FellToStump(world, x, y, mature, atTick: 0UL);
        Assert.True(world.TryGetPropAt(x, y, out MapProp stump));
        Assert.Equal(TreeGrowthStage.Stump, stump.GrowthStage);
        Assert.False(stump.HasCollision || stump.HasCanopy, "a stump has no trunk collider or canopy");

        ulong stumpEnd = trees.StageTicks(TreeGrowthStage.Stump, TickRate);
        ulong saplingEnd = stumpEnd + trees.StageTicks(TreeGrowthStage.Sapling, TickRate);
        ulong youngEnd = saplingEnd + trees.StageTicks(TreeGrowthStage.Young, TickRate);

        Assert.Equal(0, Grow(world, stumpEnd - 1));
        Assert.Equal(1, Grow(world, stumpEnd));
        Assert.Equal(TreeGrowthStage.Sapling, StageAt(world, x, y));
        Assert.Equal(0, Grow(world, saplingEnd - 1));
        Assert.Equal(1, Grow(world, saplingEnd));
        Assert.True(world.TryGetPropAt(x, y, out MapProp young));
        Assert.Equal(TreeGrowthStage.Young, young.GrowthStage);
        Assert.Equal(MathF.Round(mature.Destructibility.MaxDurability * trees.Young.Durability), young.Destructibility.MaxDurability);
        Assert.True(young.HasCollision, "a young tree has a trunk");
        Assert.Equal(1, Grow(world, youngEnd));

        Assert.True(world.TryGetPropAt(x, y, out MapProp regrown));
        Assert.Equal(mature, regrown);
        Assert.Equal(0, Grow(world, ulong.MaxValue));
    }

    [Fact]
    public void ProcessPropRespawns_ChunkNotLoaded_TheStumpStillGrows()
    {
        ChunkWorld2D source = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, MapProp mature) = FindTree(source);
        FellToStump(source, x, y, mature, atTick: 0UL);
        Assert.True(source.TryGetDelta(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y), out ChunkDelta? delta));

        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        world.ImportDelta(ChunkDelta.Deserialize(delta!.Serialize(currentTick: 0UL), currentTick: 0UL));
        Assert.False(world.TryGetChunk(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y), out _), "the chunk is not loaded");
        Assert.Equal(1, Grow(world, s_rules.Trees.StageTicks(TreeGrowthStage.Stump, TickRate)));

        Assert.Equal(TreeGrowthStage.Sapling, StageAt(world, x, y));
    }

    [Fact]
    public void SerializeDeserialize_GrowingTree_KeepsItsStageTypeAndNextStageTime()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, MapProp mature) = FindTree(world);
        FellToStump(world, x, y, mature, atTick: 100UL);
        Assert.True(world.TryGetDelta(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y), out ChunkDelta? delta));

        ChunkDelta reloaded = ChunkDelta.Deserialize(delta!.Serialize(currentTick: 150UL), currentTick: 0UL);

        int key = ChunkDelta.ToLocalIndex(Chunk2D.ToLocalCoord(x), Chunk2D.ToLocalCoord(y));
        Assert.Equal(new TreeGrowth(TreeGrowthStage.Stump, mature.Type), reloaded.GrowthAt(key));
        Assert.True(reloaded.TryGetRespawnTick(Chunk2D.ToLocalCoord(x), Chunk2D.ToLocalCoord(y), out ulong growsAt));
        Assert.Equal(100UL + s_rules.Trees.StageTicks(TreeGrowthStage.Stump, TickRate) - 150UL, growsAt);
    }

    [Fact]
    public void TryDamagePropAt_StumpHitButNotDugUp_KeepsGrowingOnSchedule()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, MapProp mature) = FindTree(world);
        FellToStump(world, x, y, mature, atTick: 0UL);

        Assert.True(world.TryDamagePropAt(x, y, 5f, byte.MaxValue, respawnAtTick: 0UL, out MapProp hit));
        Assert.Equal(TreeGrowthStage.Stump, hit.GrowthStage);
        Assert.Equal(1, Grow(world, s_rules.Trees.StageTicks(TreeGrowthStage.Stump, TickRate)));

        Assert.Equal(TreeGrowthStage.Sapling, StageAt(world, x, y));
    }

    [Fact]
    public void ProcessPropRespawns_StumpDugUp_NeverRegrowsAndFreesTheTile()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, MapProp mature) = FindTree(world);
        FellToStump(world, x, y, mature, atTick: 0UL);
        Assert.True(world.HasGrowingTreeAt(x, y), "an undug stump blocks building");

        Assert.True(world.TryDamagePropAt(x, y, float.MaxValue, byte.MaxValue, respawnAtTick: 0UL, out MapProp dug));
        Assert.True(world.TrySetTreeStageAt(x, y, TreeGrowthStage.Stump, dug.Destructibility, 0UL, out _));

        Assert.Equal(0, Grow(world, ulong.MaxValue));
        Assert.True(world.TryGetPropAt(x, y, out MapProp after));
        Assert.True(after.Destructibility.IsDestroyed, "a dug-up stump is gone for good");
        Assert.False(world.HasGrowingTreeAt(x, y), "the cleared tile can be built on");
    }

    [Fact]
    public void ProcessPropRespawns_PlayerWatching_DefersTheStage()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, MapProp mature) = FindTree(world);
        FellToStump(world, x, y, mature, atTick: 0UL);
        bool watching = true;
        bool IsWatched(int tileX, int tileY) => watching;
        ulong due = s_rules.Trees.StageTicks(TreeGrowthStage.Stump, TickRate);
        ulong retry = s_rules.BlockedRetryTicks(TickRate);

        Assert.Equal(0, world.ProcessPropRespawns(due, s_rules, TickRate, null, IsWatched, null));
        watching = false;
        Assert.Equal(0, world.ProcessPropRespawns(due + retry - 1, s_rules, TickRate, null, IsWatched, null));
        Assert.Equal(1, world.ProcessPropRespawns(due + retry, s_rules, TickRate, null, IsWatched, null));
        Assert.Equal(TreeGrowthStage.Sapling, StageAt(world, x, y));
    }

    [Fact]
    public void ChunkDeltaPacketMapper_RoundTrip_CarriesTheGrowthStageToTheClient()
    {
        ChunkWorld2D server = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, MapProp mature) = FindTree(server);
        FellToStump(server, x, y, mature, atTick: 0UL);
        Assert.True(server.TryGetDelta(Chunk2D.ToChunkCoord(x), Chunk2D.ToChunkCoord(y), out ChunkDelta? delta));
        using MapChunkDelta packet = ChunkDeltaPacketMapper.ToPacket(delta!);
        using MapChunkDelta received = MapChunkDelta.Deserialize(packet.Serialize());

        ChunkWorld2D client = new(new ProceduralWorldGenerator(Seed));
        client.ImportDelta(ChunkDeltaPacketMapper.FromPacket(received));

        Assert.True(client.TryGetPropAt(x, y, out MapProp seen));
        Assert.Equal(TreeGrowthStage.Stump, seen.GrowthStage);
        Assert.False(seen.Destructibility.IsDestroyed);
    }

    [Fact]
    public void ProcessPropRespawns_GrowingAStage_AllocatesNothing()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(Seed));
        (int x, int y, MapProp mature) = FindTree(world);
        List<(int TileX, int TileY, uint Revision, float Durability, TreeGrowthStage Stage)> changed = new(16);
        static bool Nobody(int tileX, int tileY) => false;
        Func<int, int, bool> nobody = Nobody;
        FellToStump(world, x, y, mature, atTick: 0UL);
        ulong stumpEnd = s_rules.Trees.StageTicks(TreeGrowthStage.Stump, TickRate);
        Assert.Equal(1, world.ProcessPropRespawns(stumpEnd, s_rules, TickRate, nobody, nobody, changed));

        ulong saplingEnd = stumpEnd + s_rules.Trees.StageTicks(TreeGrowthStage.Sapling, TickRate);
        changed.Clear();
        AllocationWindow open = AllocationProbe.Start();
        int grown = world.ProcessPropRespawns(saplingEnd, s_rules, TickRate, nobody, nobody, changed);
        AllocationResult window = open.Stop(1);

        Assert.Equal(1, grown);
        Assert.Equal(TreeGrowthStage.Young, changed[0].Stage);
        Assert.True(window.Bytes == 0, window.ToString());
    }

    private static int Grow(ChunkWorld2D world, ulong tick) => world.ProcessPropRespawns(tick, s_rules, TickRate, null, null, null);

    private static TreeGrowthStage StageAt(ChunkWorld2D world, int x, int y) =>
        world.TryGetPropAt(x, y, out MapProp prop) ? prop.GrowthStage : throw new InvalidOperationException("no prop");

    private static void FellToStump(ChunkWorld2D world, int x, int y, MapProp mature, ulong atTick)
    {
        TreeGrowthRules trees = s_rules.Trees;
        Assert.True(world.TrySetTreeStageAt(
            x,
            y,
            TreeGrowthStage.Stump,
            trees.StateOf(mature.Type, TreeGrowthStage.Stump),
            atTick + trees.StageTicks(TreeGrowthStage.Stump, TickRate),
            out _));
    }

    private static (int X, int Y, MapProp Prop) FindTree(ChunkWorld2D world)
    {
        for (int cy = -3; cy <= 3; cy++)
        {
            for (int cx = -3; cx <= 3; cx++)
            {
                foreach (MapProp prop in world.GetOrCreateChunk(cx, cy).Props)
                {
                    if (MapProp.IsTree(prop.Type))
                    {
                        return ((cx * Chunk2D.Size) + prop.LocalTileX, (cy * Chunk2D.Size) + prop.LocalTileY, prop);
                    }
                }
            }
        }

        throw new InvalidOperationException("no tree near the origin");
    }
}
