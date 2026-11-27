// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Screen rectangles of the top-right status strip (ADR-029 D10). The icons live in the strip that
/// <see cref="HudLayout.GetStatusStripRect"/> reserves above the clock and minimap column: one right-aligned row, as many
/// as fit (nine at the full 360 px). The details panel opens to the left of the column so the clock and map stay visible.
/// </summary>
internal static class StatusEffectStripLayout
{
    /// <summary>Icon edge length in pixels.</summary>
    public const float IconSize = 34f;

    /// <summary>Horizontal gap between icons.</summary>
    public const float Gap = 6f;

    /// <summary>Details panel width.</summary>
    public const float PanelWidth = 300f;

    /// <summary>Space between the strip edge and the icons.</summary>
    private const float Inset = 3f;

    private const float PanelGap = 8f;

    /// <summary>Half width and bottom of the top-centre target card (see <c>MonsterTargetWidget</c>: 340 x 74 at y = 14).</summary>
    private const float TargetCardHalfWidth = 170f;

    private const float TargetCardBottom = 88f;

    /// <summary>Gets how many icons fit in the reserved strip.</summary>
    public static int Capacity(int viewportWidth)
    {
        RectangleF strip = HudLayout.GetStatusStripRect(viewportWidth);
        return Math.Max(0, (int)MathF.Floor((strip.Width - (Inset * 2f) + Gap) / (IconSize + Gap)));
    }

    /// <summary>Gets the rectangle of icon <paramref name="index"/> (0 is the rightmost).</summary>
    public static RectangleF Icon(int index, int viewportWidth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        RectangleF strip = HudLayout.GetStatusStripRect(viewportWidth);
        float x = strip.X + strip.Width - Inset - ((index + 1) * IconSize) - (index * Gap);
        return new RectangleF(x, strip.Y + ((strip.Height - IconSize) * 0.5f), IconSize, IconSize);
    }

    /// <summary>Gets the index of the icon under <paramref name="point"/>, or -1.</summary>
    public static int HitTest(Vector2 point, int count, int viewportWidth)
    {
        int shown = Math.Min(count, Capacity(viewportWidth));
        for (int i = 0; i < shown; i++)
        {
            if (Icon(i, viewportWidth).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets the hover tooltip box: under the strip and right-aligned to the left edge of the clock and minimap column, so
    /// the column's own text never draws over it.
    /// </summary>
    public static RectangleF Tooltip(float width, int viewportWidth)
    {
        RectangleF strip = HudLayout.GetStatusStripRect(viewportWidth);
        RectangleF column = HudLayout.GetClockRect(viewportWidth);
        float x = MathF.Max(8f, column.X - PanelGap - width);
        return new RectangleF(x, strip.Y + strip.Height + HudLayout.Gap, width, 26f);
    }

    /// <summary>
    /// Gets the details panel rectangle: under the strip, to the left of the clock and minimap column, and below the
    /// top-centre target card when a narrow window would make them meet.
    /// </summary>
    public static RectangleF Panel(float height, int viewportWidth)
    {
        RectangleF strip = HudLayout.GetStatusStripRect(viewportWidth);
        RectangleF column = HudLayout.GetClockRect(viewportWidth);
        float x = MathF.Max(8f, column.X - PanelGap - PanelWidth);
        float y = strip.Y + strip.Height + HudLayout.Gap;
        if (x < (viewportWidth * 0.5f) + TargetCardHalfWidth + PanelGap)
        {
            y = MathF.Max(y, TargetCardBottom + PanelGap);
        }

        return new RectangleF(x, y, PanelWidth, height);
    }
}
