// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Menu;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>The character creator's name rules and turntable (docs/art/menu-ui.md rule 18).</summary>
public sealed class HeroCreatorRulesTests
{
    [Theory]
    [InlineData("K", "character.name.length")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", "character.name.length")]
    [InlineData("Hùng", "character.name.chars")]
    [InlineData("Ayla Stone", "character.name.chars")]
    [InlineData("Ay", null)]
    [InlineData("  Ayla_2  ", null)]
    public void HeroNameError_MirrorsTheServersIdentifierRule(string name, string? expected) =>
        AccountRules.HeroNameError(name).Should().Be(expected, "the client explains what the server would refuse (AuthService.IsValidIdentifier: 2-20, ASCII letters, digits, _)");

    [Fact]
    public void HeroTurntable_LeftAloneTurnsAQuarterEveryStep()
    {
        HeroTurntable turntable = new(animate: true);

        turntable.Advance(HeroTurntable.AutoStepSeconds - 0.01f);
        turntable.Index.Should().Be(0);
        turntable.Advance(0.02f);
        turntable.Index.Should().Be(1, "every side is shown in turn");
    }

    [Fact]
    public void HeroTurntable_AManualTurnPausesTheAutomaticTurning()
    {
        HeroTurntable turntable = new(animate: true);
        turntable.Turn(-1);
        turntable.Index.Should().Be(3, "left from south wraps to east");

        turntable.Advance(HeroTurntable.ResumeSeconds - 0.1f);
        turntable.Index.Should().Be(3, "the player's choice holds for a while");
        turntable.Advance(0.1f + HeroTurntable.AutoStepSeconds);
        turntable.Index.Should().Be(0, "then the turntable carries on");
    }

    [Fact]
    public void HeroTurntable_WithMotionOff_OnlyTurnsWhenAsked()
    {
        HeroTurntable turntable = new(animate: false);
        turntable.Advance(60f);
        turntable.Index.Should().Be(0);

        turntable.Turn(1);
        turntable.Index.Should().Be(1);
    }
}
