// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// The draw lists <see cref="HudOverlayRenderer"/> walks every frame, in Z order. Currency is drawn only inside the
/// inventory window (owner request), never on the main HUD; the trader window keeps its own gold line for prices.
/// </summary>
internal static class HudComposition
{
    private static readonly HudElement[] s_mainHud =
    [
        HudElement.TargetFrame,
        HudElement.Minimap,
        HudElement.Hotbar,
        HudElement.SkillBar,
        HudElement.Toasts,
    ];

    private static readonly HudElement[] s_inventoryHeader = [HudElement.Wallet];

    /// <summary>Gets the always-on HUD elements drawn over the world.</summary>
    internal static ReadOnlySpan<HudElement> MainHud => s_mainHud;

    /// <summary>Gets the elements drawn in the inventory window's header, after the window chrome.</summary>
    internal static ReadOnlySpan<HudElement> InventoryHeader => s_inventoryHeader;
}
