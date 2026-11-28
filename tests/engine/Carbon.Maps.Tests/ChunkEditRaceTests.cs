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
    public void TryDamagePropAt_ChunkRecycledWhileTheEditWaitsForTheLock_DamagesTheWrongChunk()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        (int cx, int cy) = SpawnChunk(world);
        MapProp target = world.GetOrCreateChunk(cx, cy).Props.First(p => p.Destructibility.IsDestructible && !p.Destructibility.IsDestroyed);
        int wx = (cx * Chunk2D.Size) + target.LocalTileX;
        int wy = (cy * Chunk2D.Size) + target.LocalTileY;
        bool damaged = false;

        RaceAgainstRecycling(world, cx, cy, () => damaged = world.TryDamagePropAt(wx, wy, 1f, byte.MaxValue, out _));

        world.TryGetPropAt(wx, wy, out MapProp after).Should().BeTrue();

        // Characterisation: the edit ran on the recycled instance, now the chunk FarChunks east, so the prop at its
        // coordinates did not take exactly the one point of damage.
        (damaged && after.Destructibility.CurrentDurability == target.Destructibility.CurrentDurability - 1f).Should().BeFalse();
    }

    [Fact]
    public void SetTileAtWorld_ChunkRecycledWhileTheEditWaitsForTheLock_WritesTheWrongChunk()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        (int cx, int cy) = SpawnChunk(world);
        int wx = (cx * Chunk2D.Size) + 3;
        int wy = (cy * Chunk2D.Size) + 4;
        MapTile original = world.GetTileAtWorld(wx, wy);
        MapTile edited = original with { Biome = original.Biome == BiomeType.LushPlains ? BiomeType.DeepOcean : BiomeType.LushPlains };
        MapTile farOriginal = new ChunkWorld2D(new ProceduralWorldGenerator(1337UL), TileSize).GetTileAtWorld(wx + (FarChunks * Chunk2D.Size), wy);

        RaceAgainstRecycling(world, cx, cy, () => world.SetTileAtWorld(wx, wy, in edited));

        // Characterisation: the delta is recorded for the right chunk, so it reads back right once rebuilt, but the
        // tile was written into the recycled instance, which is now the chunk FarChunks east.
        world.GetTileAtWorld(wx + (FarChunks * Chunk2D.Size), wy).Should().Be(edited);
        farOriginal.Should().NotBe(edited);
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
