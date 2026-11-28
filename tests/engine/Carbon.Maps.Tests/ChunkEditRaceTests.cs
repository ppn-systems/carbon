// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// TS-6: an edit must land on the chunk of its coordinates even when that chunk is evicted and its instance recycled for
/// other coordinates while the edit waits for the world lock. The test holds the lock itself, so the interleaving is
/// fixed: the editing thread is parked on the lock while the test evicts and recycles the chunk.
/// </summary>
public sealed class ChunkEditRaceTests
{
    private const float TileSize = 32f;
    private const int FarChunks = 50;

    [Fact]
    public void TryDamagePropAt_ChunkRecycledWhileTheEditWaitsForTheLock_DamagesThePropAtItsCoordinates()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        (int cx, int cy) = SpawnChunk(world);
        MapProp target = world.GetOrCreateChunk(cx, cy).Props.First(p => p.Destructibility.IsDestructible && !p.Destructibility.IsDestroyed);
        int wx = (cx * Chunk2D.Size) + target.LocalTileX;
        int wy = (cy * Chunk2D.Size) + target.LocalTileY;
        bool damaged = false;

        RaceAgainstRecycling(world, cx, cy, () => damaged = world.TryDamagePropAt(wx, wy, 1f, byte.MaxValue, out _));

        world.TryGetPropAt(wx, wy, out MapProp after).Should().BeTrue();

        damaged.Should().BeTrue();
        after.Destructibility.CurrentDurability.Should().Be(target.Destructibility.CurrentDurability - 1f);
    }

    [Fact]
    public void SetTileAtWorld_ChunkRecycledWhileTheEditWaitsForTheLock_WritesOnlyItsChunk()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        (int cx, int cy) = SpawnChunk(world);
        int wx = (cx * Chunk2D.Size) + 3;
        int wy = (cy * Chunk2D.Size) + 4;
        MapTile original = world.GetTileAtWorld(wx, wy);
        MapTile edited = original with { Biome = original.Biome == BiomeType.LushPlains ? BiomeType.DeepOcean : BiomeType.LushPlains };
        MapTile farOriginal = new ChunkWorld2D(new ProceduralWorldGenerator(1337UL), TileSize).GetTileAtWorld(wx + (FarChunks * Chunk2D.Size), wy);

        RaceAgainstRecycling(world, cx, cy, () => world.SetTileAtWorld(wx, wy, in edited));

        world.GetTileAtWorld(wx, wy).Should().Be(edited);
        world.GetTileAtWorld(wx + (FarChunks * Chunk2D.Size), wy).Should().Be(farOriginal, "the chunk 50 east is untouched");
        farOriginal.Should().NotBe(edited);
    }

    [Fact]
    public void TryGetPropAt_ChunkRecycledWhileTheReadWaitsForTheLock_ReadsThePropAtItsCoordinates()
    {
        (int wx, int wy) = Scout((_, prop) => prop is { } p && p.Destructibility.IsDestructible);
        ChunkWorld2D world = WorldWithOnly(wx, wy);
        world.TryGetPropAt(wx, wy, out MapProp expected).Should().BeTrue();
        bool found = false;
        MapProp read = default;

        RaceAgainstRecycling(world, Chunk2D.ToChunkCoord(wx), Chunk2D.ToChunkCoord(wy), () => found = world.TryGetPropAt(wx, wy, out read));

        found.Should().BeTrue();
        read.Should().Be(expected);
    }

    [Fact]
    public void TrySetPropStateAt_ChunkRecycledWhileTheEditWaitsForTheLock_SetsThePropAtItsCoordinates()
    {
        (int wx, int wy) = Scout((_, prop) => prop is { } p && p.Destructibility.IsDestructible && p.Destructibility.CurrentDurability > 2f);
        ChunkWorld2D world = WorldWithOnly(wx, wy);
        bool set = false;

        RaceAgainstRecycling(world, Chunk2D.ToChunkCoord(wx), Chunk2D.ToChunkCoord(wy), () => set = world.TrySetPropStateAt(wx, wy, 1f, false, out _, out _));

        set.Should().BeTrue();
        world.TryGetPropAt(wx, wy, out MapProp after).Should().BeTrue();
        after.Destructibility.CurrentDurability.Should().Be(1f);
    }

    [Fact]
    public void TrySetTreeStageAt_ChunkRecycledWhileTheEditWaitsForTheLock_SetsTheTreeAtItsCoordinates()
    {
        (int wx, int wy) = Scout((_, prop) => prop is { } p && MapProp.IsTree(p.Type) && p.GrowthStage == TreeGrowthStage.Mature);
        ChunkWorld2D world = WorldWithOnly(wx, wy);
        world.TryGetPropAt(wx, wy, out MapProp tree).Should().BeTrue();
        bool set = false;

        RaceAgainstRecycling(world, Chunk2D.ToChunkCoord(wx), Chunk2D.ToChunkCoord(wy), () =>
            set = world.TrySetTreeStageAt(wx, wy, TreeGrowthStage.Sapling, tree.Destructibility, 100UL, out _));

        set.Should().BeTrue();
        world.TryGetPropAt(wx, wy, out MapProp after).Should().BeTrue();
        after.GrowthStage.Should().Be(TreeGrowthStage.Sapling);
    }

    [Fact]
    public void TryPlantTree_ChunkRecycledWhileThePlantingWaitsForTheLock_PlantsOnItsCoordinates()
    {
        PropRegrowthRules rules = PropRegrowthRules.LoadFromJson(
            """{ "spawnForage": { "respawnSeconds": 45 }, "blockedRetrySeconds": 5, "regrowSeconds": {}, """ + PropRegrowthTests.TreesJson + "}");
        (int wx, int wy) = Scout((tile, prop) =>
            prop is null && !tile.IsSolid && tile.Biome is not (BiomeType.ShallowWater or BiomeType.DeepOcean) &&
            BiomePropTables.TryGetPlantedTree(tile.Biome, out _));
        ChunkWorld2D world = WorldWithOnly(wx, wy);
        PlantResult result = PlantResult.Occupied;

        RaceAgainstRecycling(world, Chunk2D.ToChunkCoord(wx), Chunk2D.ToChunkCoord(wy), () =>
            result = world.TryPlantTree(wx, wy, rules, currentTick: 0UL, tickRate: 10, out _));

        result.Should().Be(PlantResult.Planted);
        world.TryGetPropAt(wx, wy, out MapProp sapling).Should().BeTrue();
        sapling.GrowthStage.Should().Be(TreeGrowthStage.Sapling);
        int farX = wx + (FarChunks * Chunk2D.Size);
        bool farHadProp = WorldWithOnly(farX, wy).TryGetPropAt(farX, wy, out MapProp farOriginal);
        world.TryGetPropAt(farX, wy, out MapProp farNow).Should().Be(farHadProp);
        farNow.Should().Be(farOriginal, "nothing was planted in the chunk 50 east");
    }

    // A tile near the origin, found in a scout world, so the world under test loads only that tile's chunk.
    private static (int X, int Y) Scout(Func<MapTile, MapProp?, bool> match)
    {
        ChunkWorld2D scout = new(new ProceduralWorldGenerator(1337UL), TileSize);
        for (int cy = -3; cy <= 3; cy++)
        {
            for (int cx = -3; cx <= 3; cx++)
            {
                Chunk2D chunk = scout.GetOrCreateChunk(cx, cy);
                for (int ly = 0; ly < Chunk2D.Size; ly++)
                {
                    for (int lx = 0; lx < Chunk2D.Size; lx++)
                    {
                        int index = chunk.FindPropIndexAt(lx, ly);
                        MapProp? prop = index >= 0 ? chunk.Props[index] : null;
                        int x = (cx * Chunk2D.Size) + lx;
                        int y = (cy * Chunk2D.Size) + ly;
                        if (!chunk.IsRoad(lx, ly) && !chunk.IsBridge(lx, ly) && !scout.Buildings.TryGetStructure(x, y, out _) &&
                            match(chunk.GetTile(lx, ly), prop))
                        {
                            return (x, y);
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("No matching tile near the origin.");
    }

    private static ChunkWorld2D WorldWithOnly(int worldTileX, int worldTileY)
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        _ = world.GetOrCreateChunk(Chunk2D.ToChunkCoord(worldTileX), Chunk2D.ToChunkCoord(worldTileY));
        return world;
    }

    private static (int X, int Y) SpawnChunk(ChunkWorld2D world) =>
        (Chunk2D.ToChunkCoord(world.SpawnTile.X), Chunk2D.ToChunkCoord(world.SpawnTile.Y));

    // Holds the world lock, starts the edit on another thread and waits until it is parked on the lock, then evicts the
    // edited chunk and recycles its instance for the chunk FarChunks east, and only then lets the edit run.
    private static void RaceAgainstRecycling(ChunkWorld2D world, int cx, int cy, Action edit)
    {
        world.TryGetChunk(cx, cy, out _).Should().BeTrue("the edited chunk is resident");
        world.PooledChunkCount.Should().Be(0, "so the next chunk built reuses exactly the evicted instance");
        Thread editor = new(() => edit()) { IsBackground = true };
        lock (world.Gate)
        {
            editor.Start();
            SpinWait.SpinUntil(() => (editor.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(30))
                .Should().BeTrue("the edit is parked on the world lock");

            float chunkWorld = Chunk2D.Size * TileSize;
            Vector2 away = new((cx - FarChunks + 0.5f) * chunkWorld, (cy + 0.5f) * chunkWorld);
            world.UpdateAroundPosition(away, chunkRadius: 0, evictionRadius: 0); // loads one new chunk, then evicts the rest
            world.TryGetChunk(cx, cy, out _).Should().BeFalse("the edited chunk was evicted");
            _ = world.GetOrCreateChunk(cx + FarChunks, cy); // rebuilds the evicted instance for other coordinates
        }

        editor.Join(TimeSpan.FromSeconds(30)).Should().BeTrue();
    }
}
