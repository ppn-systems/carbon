// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Color of the <c>+N</c> enhancement label on item icons and tooltips, tiered so high levels stand out.</summary>
internal static class EnhancementLabelStyle
{
    private static readonly ColorRgba s_risky = new(56, 189, 248, 255);
    private static readonly ColorRgba s_peak = new(232, 121, 249, 255);

    /// <summary>Returns the label color for <paramref name="level"/>: warm for +1..+4, cyan for +5..+7, violet for +8 and up.</summary>
    /// <param name="level">Enhancement level.</param>
    /// <returns>The color.</returns>
    public static ColorRgba ColorFor(byte level) => level switch
    {
        >= 8 => s_peak,
        >= 5 => s_risky,
        _ => CarbonUiPalette.TextWarm,
    };
}
