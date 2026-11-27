// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Numerics;
using Carbon.Combat.Statuses.Config;
using Carbon.Desktop.Abstractions.StatusEffects;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>ADR-029 D10: the top-right status strip layout, click-to-details routing, and pulse tints.</summary>
public sealed class StatusEffectStripTests
{
    private static readonly LocalStatusEffect[] s_effects =
    [
        new(1, 2, 8f, 10f, 2f, 1, 101),
        new(2, 1, 2f, 3f, 0.6f, 1, 101),
        new(8, 1, 0f, 0f, 1.5f, 3, 0),
    ];

    [Theory]
    [InlineData(1280, 720, true)]
    [InlineData(1920, 1080, true)]
    [InlineData(1024, 600, false)]
    [InlineData(2560, 1440, true)]
    public void Icon_NineEffects_RightAlignedInTwoRowsClearOfTheMinimapAndEachOther(int width, int height, bool showMinimap)
    {
        RectangleF minimapCard = new(width - 184f, 14f, 168f, 196f);
        List<RectangleF> icons = [];
        for (int i = 0; i < 9; i++)
        {
            icons.Add(StatusEffectStripLayout.Icon(i, width, showMinimap));
        }

        (icons[0].X + icons[0].Width).Should().Be(showMinimap ? width - 192f : width - 16f, "the first effect sits at the strip's right edge");
        icons[0].Y.Should().Be(14f);
        icons[1].X.Should().Be(icons[0].X - 40f, "34 px icons with a 6 px gap grow to the left");
        icons[6].Y.Should().Be(64f, "the seventh effect wraps to a second row");
        icons[6].X.Should().Be(icons[0].X);
        for (int i = 0; i < icons.Count; i++)
        {
            icons[i].Y.Should().BeLessThan(height);
            icons[i].X.Should().BeGreaterThan(0f);
            if (showMinimap)
            {
                Overlaps(icons[i], minimapCard).Should().BeFalse($"icon {i} must not cover the minimap");
            }

            for (int j = i + 1; j < icons.Count; j++)
            {
                Overlaps(icons[i], icons[j]).Should().BeFalse($"icons {i} and {j} overlap");
            }
        }
    }

    [Fact]
    public void HitTest_IconCentreAndGap_ReturnsThatIconOrNone()
    {
        RectangleF second = StatusEffectStripLayout.Icon(1, 1280, showMinimap: true);

        StatusEffectStripLayout.HitTest(Centre(second), 3, 1280, showMinimap: true).Should().Be(1);
        StatusEffectStripLayout.HitTest(new Vector2(second.X + second.Width + 3f, second.Y + 10f), 3, 1280, showMinimap: true)
            .Should().Be(-1, "the gap between icons is not an icon");
        StatusEffectStripLayout.HitTest(Centre(StatusEffectStripLayout.Icon(3, 1280, true)), 3, 1280, showMinimap: true)
            .Should().Be(-1, "only active effects have icons");
    }

    [Fact]
    public void HandleClick_OnTheSecondIcon_OpensTheSlowDetailsAndAClickElsewhereClosesThem()
    {
        StatusEffectStripWidget strip = new();
        Vector2 slowIcon = Centre(StatusEffectStripLayout.Icon(1, 1280, showMinimap: true));

        strip.HandleClick(new Vector2(600f, 400f), s_effects, 1280, showMinimap: true).Should().BeFalse("with no panel open a world click passes through");
        strip.HandleClick(slowIcon, s_effects, 1280, showMinimap: true).Should().BeTrue();
        strip.SelectedEffectId.Should().Be(2, "the second icon is the slow");

        strip.HandleClick(Centre(StatusEffectStripLayout.Icon(2, 1280, true)), s_effects, 1280, showMinimap: true).Should().BeTrue();
        strip.SelectedEffectId.Should().Be(8, "another icon switches the details");

        strip.HandleClick(new Vector2(600f, 400f), s_effects, 1280, showMinimap: true).Should().BeTrue("the closing click is consumed");
        strip.IsPanelOpen.Should().BeFalse();
    }

    [Fact]
    public void HandleClick_SameIconTwiceAndEscape_TogglesAndCloses()
    {
        StatusEffectStripWidget strip = new();
        Vector2 poisonIcon = Centre(StatusEffectStripLayout.Icon(0, 1920, showMinimap: false));

        _ = strip.HandleClick(poisonIcon, s_effects, 1920, showMinimap: false);
        strip.SelectedEffectId.Should().Be(1);
        _ = strip.HandleClick(poisonIcon, s_effects, 1920, showMinimap: false);
        strip.IsPanelOpen.Should().BeFalse("clicking the open icon again closes it");

        _ = strip.HandleClick(poisonIcon, s_effects, 1920, showMinimap: false);
        strip.Close().Should().BeTrue("Esc closes an open panel");
        strip.Close().Should().BeFalse("and then falls through to the next Esc handler");
    }

    [Fact]
    public void ForEffect_PoisonAndBleedPulses_AreTintedByTheirKey()
    {
        StatusEffectCatalog shipped = StatusEffectCatalog.LoadShippedDefaults();

        StatusEffectTints.ForEffect(shipped, 1).Should().Be(new ColorRgba(132, 204, 22, 255), "poison numbers are green");
        StatusEffectTints.ForEffect(shipped, 3).Should().Be(new ColorRgba(220, 38, 80, 255), "bleeding numbers are crimson");
        StatusEffectTints.ForEffect(shipped, 2).Should().Be(StatusEffectTints.Other);
        StatusEffectTints.ForEffect(shipped, 999).Should().Be(StatusEffectTints.Other);
    }

    [Fact]
    public void RemainingFraction_TimedAndDerived_DrivesTheDurationRing()
    {
        s_effects[0].RemainingFraction.Should().Be(0.8f);
        s_effects[2].IsTimed.Should().BeFalse();
        s_effects[2].RemainingFraction.Should().Be(1f, "a derived effect shows a full ring");
    }

    private static Vector2 Centre(RectangleF rect) => new(rect.X + (rect.Width * 0.5f), rect.Y + (rect.Height * 0.5f));

    private static bool Overlaps(RectangleF a, RectangleF b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
