// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Rendering.Hud;
using Carbon.Graphics.Batching.Common;
using Carbon.Items.Catalog;
using System;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Owner request: gold and diamonds are not on the main screen, only inside the inventory (the trader window keeps its
/// own gold line). <see cref="HudOverlayRenderer"/> draws exactly what the <see cref="HudComposition"/> lists name.
/// </summary>
public sealed class HudCompositionTests
{
    [Fact]
    public void MainHud_DrawList_HasNoCurrency()
    {
        HudComposition.MainHud.ToArray().Should().NotContain(HudElement.Wallet, "currency is shown only inside the inventory");
        HudComposition.MainHud.ToArray().Should().Contain(HudElement.Hotbar, "the main HUD list is the one the renderer walks every frame");
    }

    [Fact]
    public void MainHud_DrawList_DrawsTheStatusStripLastSoItsPanelSitsOverTheOtherWidgets()
    {
        ReadOnlySpan<HudElement> main = HudComposition.MainHud;
        main[^1].Should().Be(HudElement.StatusEffects, "the status details panel (ADR-029) must not be covered by toasts or the minimap");
    }

    [Fact]
    public void InventoryHeader_DrawList_ShowsCurrency() =>
        HudComposition.InventoryHeader.ToArray().Should().Contain(HudElement.Wallet, "the inventory window is where balances stay visible");

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    [InlineData(1024, 600)]
    [InlineData(2560, 1440)]
    public void WalletStrip_InInventoryHeader_SitsBetweenTitleAndCloseWithoutCoveringSlots(int width, int height)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(width, height);
        RectangleF strip = WalletWidget.GetStripRect(in layout);
        RectangleF close = PlayerModalTabs.GetCloseButtonRect(in layout);
        RectangleF window = new(layout.Ox, layout.Oy, layout.PanelW, layout.PanelH);

        window.Contains(new System.Numerics.Vector2(strip.X, strip.Y)).Should().BeTrue("the counters are inside the window");
        window.Contains(new System.Numerics.Vector2(strip.Right, strip.Bottom)).Should().BeTrue("the counters are inside the window");
        strip.Right.Should().BeLessThanOrEqualTo(close.X, "the counters stay left of the close button");
        strip.X.Should().BeGreaterThan(layout.ContentX + (160f * layout.Scale), "the window title keeps its room on the left");
        strip.Bottom.Should().BeLessThanOrEqualTo(InventoryModalLayout.GetStageCardRect(in layout).Y, "the header is above the paper-doll card");

        for (int slot = 0; slot < PlayerInventoryLayout.TotalCapacity; slot++)
        {
            strip.Intersects(InventoryModalLayout.GetSlotRect(in layout, slot)).Should().BeFalse($"slot {slot} must stay clickable");
        }

        WalletWidget.GetTextOrigin(in layout, 0).X.Should().BeLessThan(WalletWidget.GetIconCenter(in layout, 1).X, "gold and diamonds get separate columns");
    }
}
