// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Streaming;
using Carbon.Testing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Maps.Tests;

/// <summary>
/// Audit round 3, ALLOC-12: a player's streaming update runs every 10 ticks on the server tick, and in the client's frame
/// update. Once the chunk pool is warm and the strip has been generated before (road sites cached, prop lists at
/// capacity), what a load step still allocates is the streamer's own bookkeeping.
/// </summary>
public sealed class ChunkStreamingAllocationTests(ITestOutputHelper output)
{
    private const int StripChunks = 24;

    [Fact]
    public void UpdateAroundPosition_WalkingAWarmStrip_RebuildsTheSnapshotPerLoadedChunk()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), tileSize: 32f);
        Walker walker = new(world);
        for (int pass = 0; pass < 6; pass++)
        {
            walker.Pass();
        }

        // One iteration is one step of one chunk: five chunks load and five are evicted.
        AllocationResult window = AllocationProbe.Measure(StripChunks * 2, warmupIterations: 0, i => walker.Step());
        output.WriteLine($"{window.BytesPerIteration:F1} B per load step ({window})");

        // Characterisation: 2,365 B per step, most of it a new resident snapshot array for every loaded chunk.
        window.BytesPerIteration.Should().BeGreaterThan(2000, $"each loaded chunk rebuilds the resident snapshot ({window})");
    }

    [Fact]
    public void UpdateAroundPosition_EveryStep_ActiveChunksHoldExactlyTheResidentChunks()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), tileSize: 32f);
        Walker walker = new(world);
        for (int step = 0; step < StripChunks * 3; step++)
        {
            walker.Step();

            ReadOnlySpan<Chunk2D> active = world.ActiveChunks;
            active.Length.Should().Be(world.LoadedChunkCount, $"step {step}: the snapshot lists every resident chunk, and only those");
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    Contains(active, walker.CenterChunkX + dx, walker.CenterChunkY + dy).Should().BeTrue(
                        $"step {step}: chunk ({walker.CenterChunkX + dx}, {walker.CenterChunkY + dy}) was just loaded");
                }
            }
        }
    }

    private static bool Contains(ReadOnlySpan<Chunk2D> chunks, int chunkX, int chunkY)
    {
        foreach (Chunk2D chunk in chunks)
        {
            if (chunk.ChunkX == chunkX && chunk.ChunkY == chunkY)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Walks east along a strip and back, one chunk per step, as a player crossing it would.</summary>
    private sealed class Walker(ChunkWorld2D world)
    {
        private readonly float _chunkWorld = Chunk2D.Size * 32f;
        private readonly Vector2 _start = new(world.SpawnTile.X * 32f, world.SpawnTile.Y * 32f);
        private int _step;

        public int CenterChunkX { get; private set; }

        public int CenterChunkY { get; private set; }

        public void Pass()
        {
            for (int i = 0; i < StripChunks * 2; i++)
            {
                this.Step();
            }
        }

        public void Step()
        {
            int along = _step % (StripChunks * 2);
            int offset = along < StripChunks ? along : (StripChunks * 2) - along;
            Vector2 position = _start + new Vector2(offset * _chunkWorld, 0f);
            world.UpdateAroundPosition(position, chunkRadius: 2, evictionRadius: 8);
            this.CenterChunkX = (int)MathF.Floor(position.X / _chunkWorld);
            this.CenterChunkY = (int)MathF.Floor(position.Y / _chunkWorld);
            _step++;
        }
    }
}
