// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Maps.Data;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// Two players far apart: each streaming step must keep the chunks of both, not evict the other player's chunks and
/// regenerate them on its next step (every 10 ticks).
/// </summary>
public sealed class MultiPlayerStreamingTests
{
    private const int Apart = 30;

    [Fact]
    public void StepFixedTick_TwoPlayersFarApart_OneOfThemHasItsChunksEvicted()
    {
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 0);
        AuthoritativeWorld world = harness.World;
        (int ax, int ay) = Enter(world, 3_001UL, 0, 0);
        (int bx, int by) = Enter(world, 3_002UL, Apart, 0);

        bool bothResidentAfterEveryStreamingTick = true;
        for (int t = 0; t < 40; t++)
        {
            world.StepFixedTick();
            bothResidentAfterEveryStreamingTick &= world.Chunks.TryGetChunk(ax, ay, out _) && world.Chunks.TryGetChunk(bx, by, out _);
        }

        // Characterisation: whichever player streams second evicts the chunk the first one stands in.
        bothResidentAfterEveryStreamingTick.Should().BeFalse();
    }

    private static (int ChunkX, int ChunkY) Enter(AuthoritativeWorld world, ulong connectionId, int chunkOffsetX, int chunkOffsetY)
    {
        Vector2 spawn = world.Chunks.SpawnWorldPosition;
        float chunkWorld = Chunk2D.Size * world.TileSize;
        int cx = (int)MathF.Floor(spawn.X / chunkWorld) + chunkOffsetX;
        int cy = (int)MathF.Floor(spawn.Y / chunkWorld) + chunkOffsetY;
        Vector2 at = new((cx + 0.5f) * chunkWorld, (cy + 0.5f) * chunkWorld);
        CharacterRecord hero = new(Guid.NewGuid(), Guid.NewGuid(), "Far" + connectionId, 1, default, at.X, at.Y, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
        world.TryEnterWorld(connectionId, hero).Status.Should().Be(OpStatus.Ok);
        return (cx, cy);
    }
}
