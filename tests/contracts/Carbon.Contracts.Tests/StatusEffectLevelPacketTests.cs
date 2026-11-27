// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Carbon.Contracts.Entities.S2C;
using Carbon.Contracts.Items.S2C;
using Xunit;

namespace Carbon.Contracts.Tests;

/// <summary>ADR-036 D4: the effect level travels in <c>StatusEffectSync</c> and in the pulse <c>CombatHit</c>.</summary>
public sealed class StatusEffectLevelPacketTests
{
    [Fact]
    public void StatusEffectSync_RoundTrip_KeepsTheLevelOfEveryEntry()
    {
        using StatusEffectSync sync = new()
        {
            EffectIds = [1, 2],
            Stacks = [2, 1],
            RemainingMs = [8000U, 2500U],
            DurationMs = [10_000U, 3000U],
            Magnitudes = [6f, 0.4f],
            SourceKinds = [1, 1],
            SourceIds = [104, 101],
            Levels = [3, 255],
        };

        using StatusEffectSync copy = StatusEffectSync.Deserialize(sync.Serialize());

        Assert.Equal(new byte[] { 3, 255 }, copy.Levels);
        Assert.Equal(new ushort[] { 1, 2 }, copy.EffectIds);
        Assert.Equal(new ushort[] { 104, 101 }, copy.SourceIds);
    }

    [Fact]
    public void CombatHit_RoundTrip_KeepsTheStatusEffectLevel()
    {
        using CombatHit hit = new() { AttackerNetworkId = 5UL, DefenderNetworkId = 6UL, Damage = 2.5f, RemainingHealth = 90f, StatusEffectId = 3, StatusEffectLevel = 2 };

        using CombatHit copy = CombatHit.Deserialize(hit.Serialize());

        Assert.Equal((ushort)3, copy.StatusEffectId);
        Assert.Equal((byte)2, copy.StatusEffectLevel);
        Assert.Equal(2.5f, copy.Damage);
    }

    [Fact]
    public void Version_IsThreeSinceStatusEffectLevels() => Assert.Equal((ushort)3, CarbonProtocol.Version);
}
