// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Application.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Maps.S2C;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Settlements;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>
/// Audit SA-10: which chunks a client may request. The client asks for the chunks around its own position; the server
/// serves only chunks within <see cref="AuthoritativeWorld.ChunkRequestReach"/> of the requester's player, so a request
/// cannot reveal distant land or make the world build a distant village under its lock.
/// </summary>
public sealed class ChunkRequestReachTests
{
    private const ulong Visitor = 2_000UL;

    [Fact]
    public void TryCreateChunkDeltaPacket_AVillageBeyondTheRequestersReach_IsRefusedAndNotBuilt()
    {
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 1);
        AuthoritativeWorld world = harness.World;
        (int cx, int cy) = FarVillageChunk(world, minChunks: 12);

        world.TryCreateChunkDeltaPacket(harness.Connections[0], cx, cy, 0U, out MapChunkDelta? packet).Should().BeFalse();
        using (packet)
        {
            packet.Should().BeNull();
        }

        world.Chunks.TryGetDelta(cx, cy, out _).Should().BeFalse("the village is not built for a refused request");
    }

    [Fact]
    public void TryCreateChunkDeltaPacket_AVillageWithinTheRequestersReach_IsBuiltAndServed()
    {
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 1);
        AuthoritativeWorld world = harness.World;
        (int cx, int cy) = FarVillageChunk(world, minChunks: 12);
        EnterAtChunk(world, Visitor, cx, cy);

        world.TryCreateChunkDeltaPacket(Visitor, cx, cy, 0U, out MapChunkDelta? packet).Should().BeTrue();
        using (packet)
        {
            packet!.StructureIndices.Should().NotBeEmpty("the village was built before its chunk was served");
        }
    }

    [Theory]
    [InlineData(3, 0, true)]
    [InlineData(0, 3, true)]
    [InlineData(3, 3, true)]
    [InlineData(4, 0, false)]
    [InlineData(0, 4, false)]
    public void TryCreateChunkDeltaPacket_AtTheEdgeOfTheReach_ServesInsideAndRefusesOutside(int chunksWest, int chunksNorth, bool served)
    {
        // The client requests the ring of 2 chunks it streams; the server allows one chunk of slack, on each axis.
        AuthoritativeWorld.ChunkRequestReach.Should().Be(3);
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 1);
        AuthoritativeWorld world = harness.World;
        (int cx, int cy) = FarVillageChunk(world, minChunks: 12);
        EnterAtChunk(world, Visitor, cx - chunksWest, cy - chunksNorth);

        // Entering near the village streams chunks of its site, which builds it either way; only the reply differs.
        world.TryCreateChunkDeltaPacket(Visitor, cx, cy, 0U, out MapChunkDelta? packet).Should().Be(served);
        packet?.Dispose();
    }

    [Fact]
    public void TryCreateChunkDeltaPacket_AConnectionWithNoPlayerInTheWorld_IsRefused()
    {
        // Not even a chunk with a delta to serve: the far village.
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 1);
        AuthoritativeWorld world = harness.World;
        (int cx, int cy) = FarVillageChunk(world, minChunks: 12);

        world.TryCreateChunkDeltaPacket(9_999UL, cx, cy, 0U, out MapChunkDelta? packet).Should().BeFalse();
        using (packet)
        {
            packet.Should().BeNull();
        }

        world.Chunks.TryGetDelta(cx, cy, out _).Should().BeFalse("nothing is built for a connection that is not in the world");
    }

    [Fact]
    public void TryCreateChunkDeltaPacket_ManyRefusals_WarnAtMostOnceAMinuteWithTheCount()
    {
        // The refusals are rate-limited in the log: a flood of bad requests cannot flood it.
        CapturingLogger logger = new();
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 1, logger: logger);
        AuthoritativeWorld world = harness.World;
        (int cx, int cy) = FarVillageChunk(world, minChunks: 12);
        for (int i = 0; i < 100; i++)
        {
            _ = world.TryCreateChunkDeltaPacket(harness.Connections[0], cx, cy, 0U, out _);
        }

        logger.Warnings.Should().ContainSingle(w => w.StartsWith("Refused 1 chunk requests", StringComparison.Ordinal));

        for (int t = 0; t < world.TickRate * 60; t++)
        {
            world.StepFixedTick();
        }

        _ = world.TryCreateChunkDeltaPacket(harness.Connections[0], cx, cy, 0U, out _);
        logger.Warnings.Should().HaveCount(2).And.Contain(w => w.StartsWith("Refused 100 chunk requests", StringComparison.Ordinal),
            "the next warning, a minute later, counts the refusals since the last one");
    }

    // The chunk of a village building at least minChunks away from the spawn chunk, on both axes.
    private static (int ChunkX, int ChunkY) FarVillageChunk(AuthoritativeWorld world, int minChunks)
    {
        ProceduralWorldGenerator generator = world.Generator;
        int spawnCx = Chunk2D.ToChunkCoord((int)MathF.Floor(world.DefaultSpawnWorldPosition.X / world.TileSize));
        int spawnCy = Chunk2D.ToChunkCoord((int)MathF.Floor(world.DefaultSpawnWorldPosition.Y / world.TileSize));
        for (int ring = 1; ring < 12; ring++)
        {
            for (int sy = -ring; sy <= ring; sy++)
            {
                for (int sx = -ring; sx <= ring; sx++)
                {
                    foreach (VillagePlot plot in generator.Landmarks.GetLayout(sx, sy).Plots)
                    {
                        int cx = Chunk2D.ToChunkCoord(plot.OriginX);
                        int cy = Chunk2D.ToChunkCoord(plot.OriginY);
                        if (Math.Abs(cx - spawnCx) >= minChunks && Math.Abs(cy - spawnCy) >= minChunks)
                        {
                            return (cx, cy);
                        }
                    }
                }
            }
        }

        throw new InvalidOperationException("no village far from spawn for the test seed");
    }

    // Enters a character at the centre of a chunk.
    private static void EnterAtChunk(AuthoritativeWorld world, ulong connectionId, int chunkX, int chunkY)
    {
        Vector2 position = new(((chunkX * Chunk2D.Size) + (Chunk2D.Size / 2)) * world.TileSize, ((chunkY * Chunk2D.Size) + (Chunk2D.Size / 2)) * world.TileSize);
        CharacterRecord hero = new(Guid.NewGuid(), Guid.NewGuid(), "Visitor", 1, default, position.X, position.Y, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
        world.TryEnterWorld(connectionId, hero).Status.Should().Be(OpStatus.Ok);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                this.Warnings.Add(formatter(state, exception));
            }
        }
    }
}
