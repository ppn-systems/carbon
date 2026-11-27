// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Contracts.Common;
using Carbon.Desktop.Rendering.Effects;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>The forge hammer animation only displays server results (ADR-020): charge-up, then the received outcome.</summary>
public sealed class ForgeEnhancementVfxTests
{
    [Fact]
    public void Update_ResultArrivesDuringChargeUp_PlaysItOnlyAfterTheThreeStrikes()
    {
        ForgeEnhancementVfx vfx = new();
        List<ForgeSfxCue> cues = [];
        vfx.SfxCue += cues.Add;
        vfx.Update(0f, resultSerial: 4, EnhancementOutcome.None);

        vfx.BeginCharge();
        vfx.Update(0.1f, resultSerial: 5, EnhancementOutcome.Destroyed);
        vfx.PlayingOutcome.Should().Be(EnhancementOutcome.None, "the hammer keeps charging until the minimum charge-up");
        vfx.HidesItem.Should().BeFalse();

        for (int i = 0; i < 9; i++)
        {
            vfx.Update(0.1f, resultSerial: 5, EnhancementOutcome.Destroyed);
        }

        vfx.PlayingOutcome.Should().Be(EnhancementOutcome.Destroyed);
        vfx.Update(0.1f, resultSerial: 5, EnhancementOutcome.Destroyed);
        vfx.HidesItem.Should().BeTrue("a shattered item disappears from the anvil");
        cues.Should().StartWith(ForgeSfxCue.ChargeStart).And.Contain(ForgeSfxCue.HammerStrike).And.EndWith(ForgeSfxCue.Shatter);
    }

    [Theory]
    [InlineData(EnhancementOutcome.Success, ForgeSfxCue.SuccessChime)]
    [InlineData(EnhancementOutcome.NoChange, ForgeSfxCue.DullClank)]
    [InlineData(EnhancementOutcome.Downgrade, ForgeSfxCue.Crack)]
    [InlineData(EnhancementOutcome.Destroyed, ForgeSfxCue.Shatter)]
    public void Update_ResultAfterChargeUp_PlaysTheOutcomeAndItsCueThenEnds(EnhancementOutcome outcome, ForgeSfxCue cue)
    {
        ForgeEnhancementVfx vfx = new();
        List<ForgeSfxCue> cues = [];
        vfx.SfxCue += cues.Add;
        vfx.Update(0f, 0, EnhancementOutcome.None);
        vfx.BeginCharge();
        vfx.Update(ForgeEnhancementVfx.ChargeDuration + 0.2f, 0, EnhancementOutcome.None);

        vfx.Update(0.016f, 1, outcome);

        vfx.PlayingOutcome.Should().Be(outcome);
        cues[^1].Should().Be(cue);
        vfx.Update(ForgeEnhancementVfx.OutcomeDuration, 1, outcome);
        vfx.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Update_FirstFrameWithAnOldResult_DoesNotReplayIt()
    {
        ForgeEnhancementVfx vfx = new();

        vfx.Update(0.016f, resultSerial: 9, EnhancementOutcome.Success);

        vfx.IsActive.Should().BeFalse("a result from before the window opened is not replayed");
    }

    [Fact]
    public void Update_NoResultForLong_StopsCharging()
    {
        ForgeEnhancementVfx vfx = new();
        vfx.Update(0f, 0, EnhancementOutcome.None);
        vfx.BeginCharge();

        vfx.Update(10f, 0, EnhancementOutcome.None);

        vfx.IsActive.Should().BeFalse("a lost reply must not lock the forge button");
    }

    [Fact]
    public void Preview_Frozen_StaysOnTheOutcomeWithoutFiringCues()
    {
        ForgeEnhancementVfx vfx = new();
        List<ForgeSfxCue> cues = [];
        vfx.SfxCue += cues.Add;

        vfx.Preview(EnhancementOutcome.Downgrade, 0.3f, freeze: true);
        vfx.Update(5f, 0, EnhancementOutcome.None);

        vfx.PlayingOutcome.Should().Be(EnhancementOutcome.Downgrade);
        vfx.IsPreview.Should().BeTrue();
        cues.Should().BeEmpty();
        vfx.GetItemOffset().X.Should().NotBe(0f, "a downgrade shakes the item");
    }
}
