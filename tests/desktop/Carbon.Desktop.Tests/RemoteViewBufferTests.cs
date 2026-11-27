// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Linq;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Networking.Session;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Audit round 2, PERF-5: the per-frame remote views reuse a buffer instead of a new array each frame.</summary>
public sealed class RemoteViewBufferTests
{
    [Fact]
    public void Add_PastInitialCapacity_KeepsEveryViewInOrder()
    {
        RemoteViewBuffer buffer = new();

        for (int i = 0; i < 200; i++)
        {
            buffer.Add(View((ulong)i));
        }

        buffer.Count.Should().Be(200);
        buffer.Select(v => v.NetworkId).Should().Equal(Enumerable.Range(0, 200).Select(i => (ulong)i));
        buffer[199].NetworkId.Should().Be(199UL);
    }

    [Fact]
    public void Clear_ThenRefill_ShowsOnlyTheNewViews()
    {
        RemoteViewBuffer buffer = new();
        buffer.Add(View(1));
        buffer.Add(View(2));

        buffer.Clear();
        buffer.Add(View(9));

        buffer.Should().ContainSingle().Which.NetworkId.Should().Be(9UL);
        FluentActions.Invoking(() => buffer[1]).Should().Throw<ArgumentOutOfRangeException>("stale slots past Count are not readable");
    }

    [Fact]
    public void Refill_AtSteadyCapacity_AllocatesNothing()
    {
        RemoteViewBuffer buffer = new();
        for (int i = 0; i < 50; i++)
        {
            buffer.Add(View((ulong)i));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int frame = 0; frame < 100; frame++)
        {
            buffer.Clear();
            for (int i = 0; i < 50; i++)
            {
                buffer.Add(View((ulong)i));
            }
        }

        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0, "the remote views are rebuilt every frame");
    }

    private static RemoteEntityView View(ulong id) =>
        new(id, EntityKind.Monster, "slime", new Vector2(id, id), Vector2.Zero, 8f, 10f, 10f);
}
