// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>A screen-space element that <see cref="HudOverlayRenderer"/> draws through a <see cref="HudComposition"/> list.</summary>
internal enum HudElement : byte
{
    /// <summary>Locked or aimed monster target frame (top centre).</summary>
    TargetFrame,

    /// <summary>Day/night clock (server world time) at the top of the right-hand column.</summary>
    Clock,

    /// <summary>Radar minimap (when enabled in the settings).</summary>
    Minimap,

    /// <summary>Vitals and the nine-slot hotbar (bottom centre).</summary>
    Hotbar,

    /// <summary>Class skill bar (right edge).</summary>
    SkillBar,

    /// <summary>Fading notification toasts.</summary>
    Toasts,

    /// <summary>Gold and diamond balances.</summary>
    Wallet,
}
