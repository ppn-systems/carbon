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
        requests.Collect(10, 20, keys).Should().Be(0, "nothing more while the player stays in the chunk");
        requests.Collect(11, 20, keys).Should().Be(5, "a step east asks only for the new column");
    }

    [Fact]
    public void Collect_InTheSameChunk_AsksNothingEvenAfterAnEmptyAnswer()
    {
        // Requests go out on a chunk change, so a forgotten empty chunk is not asked again every frame.
        ChunkDeltaRequests requests = new();
        Span<long> keys = stackalloc long[ChunkDeltaRequests.MaxPerCollect];
        _ = requests.Collect(0, 0, keys);
        requests.Completed(Chunk2D.PackKey(2, 0), ChunkRequestResult.NoDelta);

        requests.Collect(0, 0, keys).Should().Be(0);
    }

    [Fact]
    public void Completed_NoDelta_IsAskedAgainAtTheNextChunkChange()
    {
        // A chunk the server refused (the client's predicted position two chunks ahead, after a stall) answers with no
        // delta; it is asked again, so its buildings and edits still load once the player is really there.
        ChunkDeltaRequests requests = new();
        Span<long> keys = stackalloc long[ChunkDeltaRequests.MaxPerCollect];
        _ = requests.Collect(0, 0, keys);
        long refused = Chunk2D.PackKey(2, 0);
        requests.Completed(refused, ChunkRequestResult.NoDelta);

        int count = requests.Collect(1, 0, keys);

        Contains(keys, count, refused).Should().BeTrue();
    }

    [Fact]
    public void Completed_Delta_IsNeverAskedAgain()
    {
        // Its later changes arrive live; the delta is asked for once.
        ChunkDeltaRequests requests = new();
        Span<long> keys = stackalloc long[ChunkDeltaRequests.MaxPerCollect];
        _ = requests.Collect(0, 0, keys);
        long loaded = Chunk2D.PackKey(2, 0);
        requests.Completed(loaded, ChunkRequestResult.Delta);

        _ = requests.Collect(5, 0, keys);
        int count = requests.Collect(0, 0, keys);

        Contains(keys, count, loaded).Should().BeFalse();
    }

    [Fact]
    public void Completed_Failed_IsAskedAgainAtTheNextChunkChange()
    {
        ChunkDeltaRequests requests = new();
        Span<long> keys = stackalloc long[ChunkDeltaRequests.MaxPerCollect];
        _ = requests.Collect(0, 0, keys);
        long failed = Chunk2D.PackKey(1, 1);
        requests.Completed(failed, ChunkRequestResult.Failed);

        requests.Collect(0, 0, keys).Should().Be(0, "not in the same chunk");
        int count = requests.Collect(0, 1, keys);
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
