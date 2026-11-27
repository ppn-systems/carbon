// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Testing;
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
        AllocationResult window = AllocationProbe.Measure(8, warmupIterations: 24, i => generator.PopulateChunk(chunk, i % 8, 3));

        Assert.True(window.Bytes == 0, $"Regenerating 8 cached chunks allocated {window}.");
    }
}
