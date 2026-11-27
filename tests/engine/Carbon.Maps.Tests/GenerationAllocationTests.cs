// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Xunit;

namespace Carbon.Maps.Tests;

/// <summary>
/// Runs alone: <c>RiverNetwork</c> keeps its regions in a process-wide, bounded cache, and the zero-allocation check
/// holds only while the warmed region stays in it. Any test generating other regions at the same time can evict it
/// between the warm-up and the measured pass (it then rebuilds and allocates about 580 KB).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GenerationAllocationGroup
{
    /// <summary>The collection name.</summary>
    public const string Name = "Generation allocation (shared region cache)";
}

/// <summary>Zero-allocation checks on the generator's warm path (ADR-032 PR 1).</summary>
[Collection(GenerationAllocationGroup.Name)]
public sealed class GenerationAllocationTests
{
    [Fact]
    public void PopulateChunk_WarmRegion_DoesNotAllocate()
    {
        ProceduralWorldGenerator generator = new(1337UL);
        Chunk2D chunk = new(0, 0);
        for (int i = 0; i < 3; i++)
        {
            for (int chunkX = 0; chunkX < 8; chunkX++)
            {
                generator.PopulateChunk(chunk, chunkX, 3);
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int chunkX = 0; chunkX < 8; chunkX++)
        {
            generator.PopulateChunk(chunk, chunkX, 3);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated == 0, $"Regenerating 8 cached chunks allocated {allocated} bytes.");
    }
}
