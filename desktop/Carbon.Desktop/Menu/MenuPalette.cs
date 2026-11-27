// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Menu;

/// <summary>
/// Colours of the account menus (docs/art/menu-ui.md). Text colours keep at least 4.5:1 contrast on the field and panel
/// fills, the focus ring and validation icons at least 3:1 (WCAG 2.2 AA); <c>MenuVisualLanguageTests</c> checks the pairs.
/// </summary>
internal static class MenuPalette
{
    /// <summary>Placeholder text inside an empty field.</summary>
    public static readonly ColorRgba Placeholder = new(136, 150, 172, 255);

    /// <summary>Inline error text and the invalid field border.</summary>
    public static readonly ColorRgba ErrorText = new(244, 136, 136, 255);

    /// <summary>The valid-field check mark and "passwords match" text.</summary>
    public static readonly ColorRgba SuccessText = new(126, 214, 160, 255);

    /// <summary>The caps-lock warning and the "fair" strength step.</summary>
    public static readonly ColorRgba WarningText = new(246, 196, 92, 255);

    /// <summary>The keyboard focus ring drawn outside a focused control.</summary>
    public static readonly ColorRgba FocusRing = new(250, 214, 122, 255);

    /// <summary>The hover border of a field that is not focused.</summary>
    public static readonly ColorRgba HoverBorder = new(140, 152, 172, 240);

    /// <summary>Fill of a disabled button.</summary>
    public static readonly ColorRgba DisabledFill = new(24, 29, 39, 200);

    /// <summary>Border of a disabled button.</summary>
    public static readonly ColorRgba DisabledBorder = new(64, 72, 88, 190);

    /// <summary>Label text of a disabled button (still readable, clearly dimmer).</summary>
    public static readonly ColorRgba DisabledText = new(132, 142, 158, 255);

    /// <summary>Vector icon strokes inside fields (user, lock, eye).</summary>
    public static readonly ColorRgba Icon = new(150, 162, 182, 255);

    /// <summary>Hairline divider under a panel header.</summary>
    public static readonly ColorRgba Divider = new(212, 186, 130, 120);

    /// <summary>The empty track of the password strength meter.</summary>
    public static readonly ColorRgba MeterTrack = new(34, 40, 54, 255);

    /// <summary>Gets the field fill the text colours are checked against.</summary>
    public static ColorRgba FieldFill => CarbonUiPalette.SlotFill;

    /// <summary>Linear blend of two colours; <paramref name="t"/> is clamped to [0, 1].</summary>
    public static ColorRgba Lerp(ColorRgba a, ColorRgba b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new ColorRgba(
            (byte)MathF.Round(a.R + ((b.R - a.R) * t)),
            (byte)MathF.Round(a.G + ((b.G - a.G) * t)),
            (byte)MathF.Round(a.B + ((b.B - a.B) * t)),
            (byte)MathF.Round(a.A + ((b.A - a.A) * t)));
    }

    /// <summary>Returns <paramref name="color"/> with its alpha multiplied by <paramref name="alpha"/> (0..1).</summary>
    public static ColorRgba WithAlpha(ColorRgba color, float alpha) =>
        color with { A = (byte)MathF.Round(color.A * Math.Clamp(alpha, 0f, 1f)) };

    /// <summary>WCAG 2.x contrast ratio of two opaque colours (1..21).</summary>
    public static double ContrastRatio(ColorRgba a, ColorRgba b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double RelativeLuminance(ColorRgba c) =>
        (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

    private static double Channel(byte value)
    {
        double s = value / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
