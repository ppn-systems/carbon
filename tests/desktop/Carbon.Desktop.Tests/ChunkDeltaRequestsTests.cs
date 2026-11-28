// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Networking.Session;
using Carbon.Maps.Data;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Which chunk deltas the client asks the server for (audit SA-10).</summary>
public sealed class ChunkDeltaRequestsTests
{
    [Fact]
    public void Collect_TheFirstTime_RequestsTheWholeRingOnce()
    {
        ChunkDeltaRequests requests = new();
        Span<long> keys = stackalloc long[ChunkDeltaRequests.MaxPerCollect];

        requests.Collect(10, 20, keys).Should().Be(25);
        requests.Collect(10, 20, keys).Should().Be(0, "nothing is asked twice while it is outstanding or answered");
    }

    [Fact]
    public void Completed_NoDelta_IsNeverAskedAgain()
    {
        // Characterisation: a request that ends with no delta stays marked, so a chunk the server refused (the client's
        // predicted position too far ahead, after a stall) never loads its buildings or edits this session.
        ChunkDeltaRequests requests = new();
        Span<long> keys = stackalloc long[ChunkDeltaRequests.MaxPerCollect];
        _ = requests.Collect(0, 0, keys);
        long refused = Chunk2D.PackKey(2, 0);
        requests.Completed(refused, ChunkRequestResult.NoDelta);

        _ = requests.Collect(5, 0, keys); // the player moves on, then comes back

        requests.Collect(0, 0, keys).Should().Be(0, "the chunk that brought no delta is never asked again");
    }

    [Fact]
    public void Completed_Failed_IsAskedAgain()
    {
        ChunkDeltaRequests requests = new();
        Span<long> keys = stackalloc long[ChunkDeltaRequests.MaxPerCollect];
        _ = requests.Collect(0, 0, keys);
        long failed = Chunk2D.PackKey(1, 1);
        requests.Completed(failed, ChunkRequestResult.Failed);

        int count = requests.Collect(0, 0, keys);
        Contains(keys, count, failed).Should().BeTrue();
    }

    [Fact]
    public void Reset_ForgetsEveryRequest()
    {
        ChunkDeltaRequests requests = new();
        Span<long> keys = stackalloc long[ChunkDeltaRequests.MaxPerCollect];
        _ = requests.Collect(0, 0, keys);
        requests.Reset();

        requests.Collect(0, 0, keys).Should().Be(25);
    }

    private static bool Contains(ReadOnlySpan<long> keys, int count, long key) => keys[..count].Contains(key);
}
