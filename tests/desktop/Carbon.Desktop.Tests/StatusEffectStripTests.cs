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
    [InlineData(1280)]
    [InlineData(1920)]
    [InlineData(1024)]
    [InlineData(2560)]
    public void Icon_NineEffects_FillOneRightAlignedRowInsideTheReservedStripClearOfTheClockAndMinimap(int width)
    {
        RectangleF strip = HudLayout.GetStatusStripRect(width);
        RectangleF clock = HudLayout.GetClockRect(width);
        RectangleF minimap = HudLayout.GetMinimapFrameRect(width);
        StatusEffectStripLayout.Capacity(width).Should().Be(9, "nine 34 px icons with 6 px gaps fill the 360 px strip");

        List<RectangleF> icons = [];
        for (int i = 0; i < 9; i++)
        {
            icons.Add(StatusEffectStripLayout.Icon(i, width));
        }

        (icons[0].X + icons[0].Width).Should().Be(strip.X + strip.Width - 3f, "the first effect sits at the strip's right edge");
        icons[0].Y.Should().Be(11f, "icons are centred in the 40 px strip at y = 8");
        icons[1].X.Should().Be(icons[0].X - 40f, "icons grow to the left at a 40 px pitch");
        for (int i = 0; i < icons.Count; i++)
        {
            Contains(strip, icons[i]).Should().BeTrue($"icon {i} must stay inside the strip HudLayout reserves");
            Overlaps(icons[i], clock).Should().BeFalse($"icon {i} must not cover the clock");
            Overlaps(icons[i], minimap).Should().BeFalse($"icon {i} must not cover the minimap");
            for (int j = i + 1; j < icons.Count; j++)
            {
                Overlaps(icons[i], icons[j]).Should().BeFalse($"icons {i} and {j} overlap");
            }
        }
    }

    [Theory]
    [InlineData(1280, 96f)]
    [InlineData(1920, 54f)]
    [InlineData(2560, 54f)]
    public void Panel_OpensUnderTheStripLeftOfTheClockAndMinimapColumnAndClearOfTheTargetCard(int width, float expectedTop)
    {
        RectangleF panel = StatusEffectStripLayout.Panel(160f, width);
        RectangleF targetCard = new(System.MathF.Floor((width - 340f) * 0.5f), 14f, 340f, 74f);

        panel.Y.Should().Be(expectedTop, "the panel drops below the target card only when they would meet");
        Overlaps(panel, targetCard).Should().BeFalse("the locked target stays readable");
        Overlaps(panel, HudLayout.GetClockRect(width)).Should().BeFalse("the clock stays readable while the details are open");
        Overlaps(panel, HudLayout.GetMinimapFrameRect(width)).Should().BeFalse("so does the minimap");
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(1920)]
    [InlineData(1024)]
    public void Tooltip_SitsUnderTheStripLeftOfTheColumnAndClearOfTheTargetCard(int width)
    {
        RectangleF tooltip = StatusEffectStripLayout.Tooltip(120f, width);
        RectangleF targetCard = new(System.MathF.Floor((width - 340f) * 0.5f), 14f, 340f, 74f);

        (tooltip.X + tooltip.Width).Should().Be(HudLayout.GetClockRect(width).X - 8f);
        tooltip.Y.Should().Be(54f);
        Overlaps(tooltip, HudLayout.GetClockRect(width)).Should().BeFalse("the clock's text would draw over the tooltip");
        Overlaps(tooltip, HudLayout.GetMinimapFrameRect(width)).Should().BeFalse();
        Overlaps(tooltip, targetCard).Should().BeFalse();
    }

    [Fact]
    public void HitTest_NarrowWindow_OnlyIconsThatFitAreClickable()
    {
        int width = 300;
        int fit = StatusEffectStripLayout.Capacity(width);
        fit.Should().Be(6, "a 268 px strip holds six icons");
        StatusEffectStripLayout.HitTest(Centre(StatusEffectStripLayout.Icon(fit, width)), 9, width).Should().Be(-1, "an icon that does not fit is not drawn or clickable");
    }

    [Fact]
    public void HitTest_IconCentreAndGap_ReturnsThatIconOrNone()
    {
        RectangleF second = StatusEffectStripLayout.Icon(1, 1280);

        StatusEffectStripLayout.HitTest(Centre(second), 3, 1280).Should().Be(1);
        StatusEffectStripLayout.HitTest(new Vector2(second.X + second.Width + 3f, second.Y + 10f), 3, 1280)
            .Should().Be(-1, "the gap between icons is not an icon");
        StatusEffectStripLayout.HitTest(Centre(StatusEffectStripLayout.Icon(3, 1280)), 3, 1280)
            .Should().Be(-1, "only active effects have icons");
    }

    [Fact]
    public void HandleClick_OnTheSecondIcon_OpensTheSlowDetailsAndAClickElsewhereClosesThem()
    {
        StatusEffectStripWidget strip = new();
        Vector2 slowIcon = Centre(StatusEffectStripLayout.Icon(1, 1280));

        strip.HandleClick(new Vector2(600f, 400f), s_effects, 1280).Should().BeFalse("with no panel open a world click passes through");
        strip.HandleClick(slowIcon, s_effects, 1280).Should().BeTrue();
        strip.SelectedEffectId.Should().Be(2, "the second icon is the slow");

        strip.HandleClick(Centre(StatusEffectStripLayout.Icon(2, 1280)), s_effects, 1280).Should().BeTrue();
        strip.SelectedEffectId.Should().Be(8, "another icon switches the details");

        strip.HandleClick(new Vector2(600f, 400f), s_effects, 1280).Should().BeTrue("the closing click is consumed");
        strip.IsPanelOpen.Should().BeFalse();
    }

    [Fact]
    public void HandleClick_SameIconTwiceAndEscape_TogglesAndCloses()
    {
        StatusEffectStripWidget strip = new();
        Vector2 poisonIcon = Centre(StatusEffectStripLayout.Icon(0, 1920));

        _ = strip.HandleClick(poisonIcon, s_effects, 1920);
        strip.SelectedEffectId.Should().Be(1);
        _ = strip.HandleClick(poisonIcon, s_effects, 1920);
        strip.IsPanelOpen.Should().BeFalse("clicking the open icon again closes it");

        _ = strip.HandleClick(poisonIcon, s_effects, 1920);
        strip.Close().Should().BeTrue("Esc closes an open panel");
        strip.Close().Should().BeFalse("and then falls through to the next Esc handler");
    }

    [Fact]
    public void Suppressed_WhileTheDeathScreenIsUp_ClosesThePanelAndLetsClicksThroughToRespawn()
    {
        StatusEffectStripWidget strip = new();
        Vector2 poisonIcon = Centre(StatusEffectStripLayout.Icon(0, 1280));
        _ = strip.HandleClick(poisonIcon, s_effects, 1280);
        strip.IsPanelOpen.Should().BeTrue();

        strip.Suppressed = true;

        strip.IsPanelOpen.Should().BeFalse("the death screen goes on top (ADR-031)");
        strip.HandleClick(poisonIcon, s_effects, 1280).Should().BeFalse("a click on a hidden icon belongs to the death screen");
        strip.IsOver(poisonIcon, s_effects, 1280).Should().BeFalse("holding attack over the hidden strip is not blocked by it");

        strip.Suppressed = false;
        strip.HandleClick(poisonIcon, s_effects, 1280).Should().BeTrue("after the respawn the strip takes clicks again");
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

    private static bool Contains(RectangleF outer, RectangleF inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;

    private static bool Overlaps(RectangleF a, RectangleF b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}
