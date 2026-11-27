// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Hud;

namespace Carbon.Desktop.Capture;

/// <summary>
/// Opens the HUD window named by <c>CARBON_CAPTURE_MODAL</c> before a headless capture (ADR-023 D8):
/// <c>settings</c>, <c>controls</c>, <c>inventory</c>, <c>character</c>, <c>skills</c> or <c>crafting</c>.
/// </summary>
internal static class CaptureModalOpener
{
    /// <summary>Opens <paramref name="modal"/> on <paramref name="hud"/>; unknown names open the Settings window.</summary>
    /// <param name="hud">The HUD renderer.</param>
    /// <param name="world">The world read model (player-window tabs check class skills).</param>
    /// <param name="modal">The knob value.</param>
    public static void Open(HudOverlayRenderer hud, IWorldReadModel world, string modal)
    {
        ArgumentNullException.ThrowIfNull(hud);
        ArgumentNullException.ThrowIfNull(world);
        PlayerModalTab? tab = modal switch
        {
            "inventory" => PlayerModalTab.Inventory,
            "character" => PlayerModalTab.Character,
            "skills" => PlayerModalTab.Skills,
            "crafting" => PlayerModalTab.Crafting,
            _ => null,
        };

        if (tab is { } playerTab)
        {
            _ = hud.OpenOrTogglePlayerTab(world, playerTab);
            return;
        }

        hud.OpenSettings(string.Equals(modal, "controls", StringComparison.Ordinal) ? 1 : 0);
    }
}
