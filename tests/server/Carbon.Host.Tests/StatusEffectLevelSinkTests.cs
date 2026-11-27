// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Abstractions.World;
using Carbon.Contracts.Entities.S2C;
using Carbon.Contracts.Items.S2C;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

/// <summary>ADR-036 D4: the outbound sink copies each effect's level into the packets the client reads.</summary>
public sealed class StatusEffectLevelSinkTests
{
    [Fact]
    public void PopulateStatusEffectSync_Entries_CopiesTheLevelPerEntry()
    {
        using StatusEffectSync pkt = new();
        StatusEffectEntry[] entries =
        [
            new(1, 2, 8000U, 10_000U, 6f, 1, 104, Level: 3),
            new(5, 1, 0U, 0U, 1f, 3, 0),
        ];

        WorldOutboundSink.PopulateStatusEffectSync(pkt, entries);

        pkt.Levels.Should().Equal([3, 1], "a derived effect is level I");
        pkt.EffectIds.Should().Equal([1, 5]);
        pkt.SourceIds.Should().Equal([104, 0]);
    }

    [Fact]
    public void PopulateCombatHit_RecycledPacket_TakesTheNewLevelEachTime()
    {
        using CombatHit pkt = new();

        WorldOutboundSink.PopulateCombatHit(pkt, 1UL, 2UL, 2.5f, 90f, false, false, statusEffectId: 3, statusEffectLevel: 2);
        (pkt.StatusEffectId, pkt.StatusEffectLevel).Should().Be((3, 2));

        WorldOutboundSink.PopulateCombatHit(pkt, 1UL, 2UL, 5f, 85f, false, false, statusEffectId: 0, statusEffectLevel: 0);
        (pkt.StatusEffectId, pkt.StatusEffectLevel).Should().Be((0, 0), "a direct hit carries no level");
    }
}
