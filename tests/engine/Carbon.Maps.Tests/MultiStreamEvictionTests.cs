// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Linq;
using System.Numerics;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Testing;
using FluentAssertions;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// The server streams once per player, each with its own position (<c>stream</c> = its connection). Eviction must keep
/// every chunk some player still keeps, or two players far apart evict and regenerate each other's chunks every step.
/// </summary>
public sealed class MultiStreamEvictionTests
{
    private const float TileSize = 32f;
    private const int LoadRadius = 2;
    private const int EvictRadius = 8;
    private const int Apart = 30; // chunks between the two players, beyond 2 x EvictRadius

    [Fact]
    public void UpdateAroundPosition_TwoPlayersFarApart_KeepsBothPlayersChunksWithoutRebuildingThem()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        Step(world, 1UL, 0, 0);
        Step(world, 2UL, Apart, 0);
        world.TryGetChunk(Apart, 0, out Chunk2D? nearTwo).Should().BeTrue();
        world.TryGetChunk(0, 0, out Chunk2D? nearOne).Should().BeTrue();
        int resident = world.LoadedChunkCount;

        for (int i = 0; i < 4; i++)
        {
            Step(world, 1UL, 0, 0);
            Step(world, 2UL, Apart, 0);
        }

        world.TryGetChunk(Apart, 0, out Chunk2D? nearTwoAfter).Should().BeTrue();
        world.TryGetChunk(0, 0, out Chunk2D? nearOneAfter).Should().BeTrue();
        nearTwoAfter.Should().BeSameAs(nearTwo, "player 1's steps never evicted it, so it was never rebuilt");
        nearOneAfter.Should().BeSameAs(nearOne);
        world.LoadedChunkCount.Should().Be(resident, "both players' chunks stay; nothing else was loaded");
    }

    [Fact]
    public void ReleaseStream_APlayerLeft_TheNextStepEvictsItsChunks()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        Step(world, 1UL, 0, 0);
        Step(world, 2UL, Apart, 0);

        world.ReleaseStream(2UL);
        Step(world, 1UL, 0, 0);

        world.TryGetChunk(Apart, 0, out _).Should().BeFalse("no player keeps it any more");
        world.TryGetChunk(0, 0, out _).Should().BeTrue();
    }

    [Fact]
    public void ChunkEvicted_ChunkAnotherPlayerKeeps_FiresOnlyOnceThatPlayerLeaves()
    {
        // ALLOC-7's per-chunk caches end on this event, so it must follow the union keep, not one player's radius.
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        long kept = Chunk2D.PackKey(Apart, 0);
        int evictions = 0;
        world.ChunkEvicted += key => evictions += key == kept ? 1 : 0;
        Step(world, 1UL, 0, 0);
        Step(world, 2UL, Apart, 0);

        Step(world, 1UL, 0, 0);
        evictions.Should().Be(0, "player 2 still keeps it");

        world.ReleaseStream(2UL);
        Step(world, 1UL, 0, 0);
        evictions.Should().Be(1);
    }

    [Fact]
    public void UpdateAroundPosition_AnonymousStream_IsNotRemembered()
    {
        // Stream 0 (the client, the server's startup call at the spawn) keeps nothing once a later step moves on, so the
        // startup area is not held resident for ever.
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        Step(world, 0UL, 0, 0);
        Step(world, 1UL, Apart, 0);

        world.TryGetChunk(0, 0, out _).Should().BeFalse();
        world.TryGetChunk(Apart, 0, out _).Should().BeTrue();
    }

    [Fact]
    public void UpdateAroundPosition_TwoPlayersFarApart_KeepsAnEditNearTheOtherPlayer()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        Step(world, 1UL, 0, 0);
        Step(world, 2UL, Apart, 0);
        Chunk2D chunk = world.GetOrCreateChunk(Apart, 0);
        MapProp target = chunk.Props.First(p => p.Destructibility.IsDestructible && !p.Destructibility.IsDestroyed);
        int wx = (Apart * Chunk2D.Size) + target.LocalTileX;
        int wy = target.LocalTileY;
        world.TryDamagePropAt(wx, wy, 1f, byte.MaxValue, out _).Should().BeTrue();

        for (int i = 0; i < 4; i++)
        {
            Step(world, 1UL, 0, 0);
            Step(world, 2UL, Apart, 0);
        }

        // Every chunk edit is recorded in the delta store and re-applied on rebuild, so no eviction loses it.
        world.TryGetPropAt(wx, wy, out MapProp after).Should().BeTrue();
        after.Destructibility.CurrentDurability.Should().Be(target.Destructibility.CurrentDurability - 1f);
    }

    [Fact]
    public void UpdateAroundPosition_TwoStillPlayersFarApart_AllocatesNothing()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), TileSize);
        Step(world, 1UL, 0, 0);
        Step(world, 2UL, Apart, 0);

        // Nothing loads or evicts: the eviction pass checks every resident chunk against both players' keeps.
        AllocationResult window = AllocationProbe.Measure(200, warmupIterations: 20, i =>
        {
            Step(world, 1UL, 0, 0);
            Step(world, 2UL, Apart, 0);
        });

        window.Bytes.Should().Be(0, $"a streaming step that changes nothing allocates nothing ({window})");
    }

    private static void Step(ChunkWorld2D world, ulong stream, int chunkX, int chunkY)
    {
        float chunkWorld = Chunk2D.Size * TileSize;
        world.UpdateAroundPosition(new Vector2((chunkX + 0.5f) * chunkWorld, (chunkY + 0.5f) * chunkWorld), LoadRadius, EvictRadius, stream);
    }
}
