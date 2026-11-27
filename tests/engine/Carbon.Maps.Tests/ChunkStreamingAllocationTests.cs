// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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
/// capacity), what a load step still allocates is the streamer's own bookkeeping. Runs in the generation group: the
/// strip's hydrology regions live in <c>RiverNetwork</c>'s process-wide cache, which other tests would trim.
/// </summary>
[Collection(GenerationAllocationGroup.Name)]
public sealed class ChunkStreamingAllocationTests(ITestOutputHelper output)
{
    private const int StripChunks = 24;

    /// <summary>
    /// Allowed average bytes per load step. Measured at 2,365 before ALLOC-12, when the resident snapshot was rebuilt for
    /// every loaded chunk, and 535 after: one snapshot array per step and the dictionary nodes of the loaded chunks.
    /// Lower this as allocations are removed; never raise it.
    /// </summary>
    private const double MaxBytesPerLoadStep = 600;

    [Fact]
    public void UpdateAroundPosition_WalkingAWarmStrip_StaysWithinTheLoadStepBudget()
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

        window.BytesPerIteration.Should().BeLessThanOrEqualTo(MaxBytesPerLoadStep,
            $"a streaming step rebuilds the resident snapshot once, not once per loaded chunk ({window})");
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

    [Fact]
    public void UpdateAroundPosition_EveryStep_BuildsOneSnapshotWhenTheResidentSetChanges()
    {
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), tileSize: 32f);
        Walker walker = new(world);
        walker.Pass();
        int changedSteps = 0;
        for (int step = 0; step < StripChunks * 2; step++)
        {
            HashSet<long> before = ResidentKeys(world);
            int buildsBefore = world.SnapshotBuilds;
            walker.Step();

            bool changed = !before.SetEquals(ResidentKeys(world));
            changedSteps += changed ? 1 : 0;
            world.SnapshotBuilds.Should().Be(buildsBefore + (changed ? 1 : 0),
                $"step {step}: one snapshot when chunks load or are evicted, none otherwise, never one per loaded chunk");
        }

        changedSteps.Should().BeGreaterThan(StripChunks / 2, "the walk loads and evicts chunks");
    }

    private static HashSet<long> ResidentKeys(ChunkWorld2D world)
    {
        HashSet<long> keys = [];
        foreach (Chunk2D chunk in world.ActiveChunks)
        {
            _ = keys.Add(chunk.Key);
        }

        return keys;
    }

    [Fact]
    public void UpdateAroundPosition_AReaderHoldingTheOldSnapshot_StillSeesTheSameArray()
    {
        // Readers walk ActiveChunks without the lock, so a step must publish a new array and leave the old one alone.
        ChunkWorld2D world = new(new ProceduralWorldGenerator(1337UL), tileSize: 32f);
        Walker walker = new(world);
        walker.Pass();
        int changedSteps = 0;
        for (int step = 0; step < StripChunks; step++)
        {
            ReadOnlySpan<Chunk2D> held = world.ActiveChunks;
            Chunk2D[] copy = held.ToArray();
            HashSet<long> before = ResidentKeys(world);
            walker.Step();
            bool changed = !before.SetEquals(ResidentKeys(world));

            held.Length.Should().Be(copy.Length, $"step {step}: the held snapshot keeps its length");
            for (int i = 0; i < copy.Length; i++)
            {
                held[i].Should().BeSameAs(copy[i], $"step {step}: the held snapshot is never written");
            }

            Unsafe.AreSame(ref MemoryMarshal.GetReference(held), ref MemoryMarshal.GetReference(world.ActiveChunks))
                .Should().Be(!changed, $"step {step}: a changed resident set is published as a new array, an unchanged one is not rebuilt");
            changedSteps += changed ? 1 : 0;
        }

        changedSteps.Should().BeGreaterThan(StripChunks / 2, "the walk loads and evicts chunks");
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
