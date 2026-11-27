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
        HudElement.Clock,
        HudElement.Minimap,
        HudElement.Hotbar,
        HudElement.SkillBar,
        HudElement.InteractPrompt,
        HudElement.Toasts,
    ];

    private static readonly HudElement[] s_inventoryHeader = [HudElement.Wallet];

    private static readonly HudElement[] s_deathVeil = [HudElement.DeathVeil];

    private static readonly HudElement[] s_deathScreen = [HudElement.DeathScreen];

    /// <summary>Gets the always-on HUD elements drawn over the world.</summary>
    internal static ReadOnlySpan<HudElement> MainHud => s_mainHud;

    /// <summary>Gets the elements drawn in the inventory window's header, after the window chrome.</summary>
    internal static ReadOnlySpan<HudElement> InventoryHeader => s_inventoryHeader;

    /// <summary>Gets the veil drawn over everything while the player is dead, before <see cref="DeathScreen"/> (ADR-031).</summary>
    internal static ReadOnlySpan<HudElement> DeathVeil => s_deathVeil;

    /// <summary>Gets the death screen drawn on top of the veil while the player is dead (ADR-031).</summary>
    internal static ReadOnlySpan<HudElement> DeathScreen => s_deathScreen;
}
