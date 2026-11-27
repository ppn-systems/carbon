// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// The enhancement tier table: thresholds and pre-baked colours for <c>+1..+10</c> items, used by the inventory, hotbar,
/// equipment slots, tooltips and the forge. Lookups and the <c>+N</c> badge strings never allocate.
/// </summary>
internal static class EnhancementTierStyles
{
    /// <summary>Highest enhancement level with its own badge string.</summary>
    public const byte MaxLevel = 10;

    private static readonly EnhancementTierStyle s_none = new(0, 0, 0, string.Empty, CarbonUiPalette.SlotFill, CarbonUiPalette.TextWarm, default, false);

    /// <summary>Tier rows in ascending level order (data, not scattered constants).</summary>
    private static readonly EnhancementTierStyle[] s_tiers =
    [
        new(1, 1, 3, "Honed", new ColorRgba(28, 62, 38, 246), new ColorRgba(116, 206, 128, 255), default, false),
        new(2, 4, 6, "Tempered", new ColorRgba(22, 46, 88, 246), new ColorRgba(104, 168, 255, 255), default, false),
        new(3, 7, 8, "Runed", new ColorRgba(56, 30, 92, 248), new ColorRgba(194, 128, 255, 255), new ColorRgba(170, 104, 240, 120), false),
        new(4, 9, 9, "Exalted", new ColorRgba(92, 62, 16, 248), new ColorRgba(255, 198, 70, 255), new ColorRgba(255, 180, 60, 130), false),
        new(5, 10, 10, "Mythic", new ColorRgba(100, 24, 26, 250), new ColorRgba(255, 120, 84, 255), new ColorRgba(255, 150, 70, 150), true),
    ];

    private static readonly string[] s_badges = CreateBadges();

    /// <summary>Gets every tier row, ascending.</summary>
    public static ReadOnlySpan<EnhancementTierStyle> All => s_tiers;

    /// <summary>Gets the style of <paramref name="level"/> (levels above 10 use the top tier; 0 is "not enhanced").</summary>
    public static ref readonly EnhancementTierStyle Get(byte level)
    {
        if (level == 0)
        {
            return ref s_none;
        }

        for (int i = 0; i < s_tiers.Length; i++)
        {
            if (level <= s_tiers[i].MaxLevel)
            {
                return ref s_tiers[i];
            }
        }

        return ref s_tiers[^1];
    }

    /// <summary>Gets the cached <c>+N</c> badge text for <paramref name="level"/> (empty for 0).</summary>
    public static string Badge(byte level) => s_badges[Math.Min(level, MaxLevel)];

    /// <summary>Fades <paramref name="color"/> to <paramref name="alphaScale"/> of its alpha (glow rings).</summary>
    public static ColorRgba Fade(ColorRgba color, float alphaScale) =>
        new(color.R, color.G, color.B, (byte)Math.Clamp(color.A * alphaScale, 0f, 255f));

    private static string[] CreateBadges()
    {
        string[] badges = new string[MaxLevel + 1];
        badges[0] = string.Empty;
        for (int i = 1; i <= MaxLevel; i++)
        {
            badges[i] = "+" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return badges;
    }
}
