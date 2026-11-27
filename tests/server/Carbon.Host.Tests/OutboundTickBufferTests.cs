// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Application.World.Replication;
using Carbon.Items.Containers;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class OutboundTickBufferTests
{
    [Fact]
    public void Dispatch_BeforeSwap_SendsNothing()
    {
        OutboundTickBuffer buffer = new();
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        buffer.Pending.SlotChanges.Add(new OutboundSlotChange(7UL, 3, new ItemSlot(4U, 2)));

        buffer.Dispatch(sink);

        sink.SlotChanges.Should().BeEmpty("staged messages are only sent after the tick swaps them to the dispatch side");
    }

    [Fact]
    public void Swap_ThenDispatch_SendsStagedMessagesOnce()
    {
        OutboundTickBuffer buffer = new();
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        buffer.Pending.SlotChanges.Add(new OutboundSlotChange(7UL, 3, new ItemSlot(4U, 2)));
        buffer.Pending.Despawns.Add(new OutboundDespawn(7UL, 1234UL));

        buffer.Swap();
        buffer.Pending.SlotChanges.Should().BeEmpty("the next tick stages into a fresh batch");
        buffer.Dispatch(sink);
        buffer.Dispatch(sink);

        sink.SlotChanges.Should().ContainSingle().Which.Should().Be((7UL, 3, 4U, 2));
        sink.Despawns.Should().ContainSingle().Which.Should().Be((7UL, 1234UL));
    }

    [Fact]
    public void Swap_RepeatedAcrossTicks_ReusesBatchesWithoutLeakingOldMessages()
    {
        OutboundTickBuffer buffer = new();
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();

        buffer.Pending.Despawns.Add(new OutboundDespawn(1UL, 1UL));
        buffer.Swap();
        buffer.Dispatch(sink);

        buffer.Pending.Despawns.Add(new OutboundDespawn(2UL, 2UL));
        buffer.Swap();
        buffer.Dispatch(sink);

        sink.Despawns.Should().Equal((1UL, 1UL), (2UL, 2UL));
    }
}
