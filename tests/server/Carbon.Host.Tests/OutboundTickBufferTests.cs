// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Application.World.Replication;
using Carbon.Contracts.Common;
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

    [Fact]
    public void Dispatch_TrimmedMessages_ForwardEachRemainingFieldInPlace()
    {
        // ADR-038: these sink calls lost the values the wire no longer carries; what is left must still line up.
        OutboundTickBuffer buffer = new();
        AuthoritativeWorldMultiplayerTests.RecordingOutboundSink sink = new();
        buffer.Pending.Recipients.Add(9UL);
        buffer.Pending.PropChanges.Add(new OutboundPropChange(0, 1, -3, 8, 12.5f, IsDestroyed: false));
        buffer.Pending.StructureChanges.Add(new OutboundStructureChange(0, 1, 4, -6, 7, 1));
        buffer.Pending.DurabilityAlerts.Add(new OutboundDurabilityAlert(9UL, DurabilityAlertKind.AboutToBreak, 18U, 5));
        buffer.Pending.EnhancementAnnouncements.Add(new OutboundEnhancementAnnouncement(0, 1, "Smith", 31, EnhancementOutcome.Success, 8));
        buffer.Pending.SkillCasts.Add(new OutboundSkillCast(0, 1, 55UL, 2, 40f, 60f));

        buffer.Swap();
        buffer.Dispatch(sink);

        sink.PropChanges.Should().ContainSingle().Which.Should().Be((-3, 8, 12.5f));
        sink.StructureChanges.Should().ContainSingle().Which.Should().Be((4, -6, 7, 1));
        sink.DurabilityAlerts.Should().ContainSingle().Which.Should().Be((9UL, DurabilityAlertKind.AboutToBreak, 18U, 5));
        (ulong[] recipients, string name, ushort itemId, EnhancementOutcome outcome, byte level) = sink.Announcements.Should().ContainSingle().Which;
        recipients.Should().Equal(9UL);
        (name, itemId, outcome, level).Should().Be(("Smith", 31, EnhancementOutcome.Success, 8));
        sink.SkillCasts.Should().ContainSingle().Which.Should().Be((55UL, 2));
    }
}
