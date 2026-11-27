// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Screen rectangles of the top-right status strip (ADR-029 D10). The icons sit under the minimap and clock column
/// (<see cref="HudLayout.GetStatusStripRect"/>), right-aligned and no wider than it: <see cref="IconsPerRow"/> icons a
/// row, wrapping onto up to <see cref="MaxRows"/> rows. The tooltip and details panel open to the left of the strip, so
/// the minimap and clock stay visible.
/// </summary>
internal static class StatusEffectStripLayout
{
    /// <summary>Icon edge length in pixels.</summary>
    public const float IconSize = 34f;

    /// <summary>Horizontal gap between icons.</summary>
    public const float Gap = 6f;

    /// <summary>Details panel width.</summary>
    public const float PanelWidth = 300f;

    /// <summary>Most rows the strip wraps onto.</summary>
    public const int MaxRows = 3;

    /// <summary>Height of the hover tooltip.</summary>
    public const float TooltipHeight = 26f;

    /// <summary>Space between the strip edge and the icons.</summary>
    private const float Inset = 3f;

    private const float PanelGap = 8f;

    /// <summary>Half width and bottom of the top-centre target card (see <c>MonsterTargetWidget</c>: 340 x 74 at y = 14).</summary>
    private const float TargetCardHalfWidth = 170f;

    private const float TargetCardBottom = 88f;

    /// <summary>Gets how many icons fit on one row of the strip.</summary>
    public static int IconsPerRow(int viewportWidth)
    {
        RectangleF strip = HudLayout.GetStatusStripRect(viewportWidth, showMinimap: true, rows: 1);
        return Math.Max(0, (int)MathF.Floor((strip.Width - (Inset * 2f) + Gap) / (IconSize + Gap)));
    }

    /// <summary>Gets how many icons the strip shows at most.</summary>
    public static int Capacity(int viewportWidth) => IconsPerRow(viewportWidth) * MaxRows;

    /// <summary>Gets how many rows <paramref name="count"/> icons fill (0 for none).</summary>
    public static int Rows(int count, int viewportWidth)
    {
        int perRow = IconsPerRow(viewportWidth);
        int shown = Math.Min(Math.Max(0, count), perRow * MaxRows);
        return perRow == 0 ? 0 : (shown + perRow - 1) / perRow;
    }

    /// <summary>Gets the rectangle of icon <paramref name="index"/>: 0 is the rightmost of the first row, rows fill right to left.</summary>
    public static RectangleF Icon(int index, int viewportWidth, bool showMinimap)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        int perRow = Math.Max(1, IconsPerRow(viewportWidth));
        int row = index / perRow;
        int column = index % perRow;
        RectangleF strip = HudLayout.GetStatusStripRect(viewportWidth, showMinimap, row + 1);
        float x = strip.X + strip.Width - Inset - ((column + 1) * IconSize) - (column * Gap);
        float y = strip.Y + (row * HudLayout.StatusRowHeight) + ((HudLayout.StatusRowHeight - IconSize) * 0.5f);
        return new RectangleF(x, y, IconSize, IconSize);
    }

    /// <summary>Gets the index of the icon under <paramref name="point"/>, or -1.</summary>
    public static int HitTest(Vector2 point, int count, int viewportWidth, bool showMinimap)
    {
        int shown = Math.Min(count, Capacity(viewportWidth));
        for (int i = 0; i < shown; i++)
        {
            if (Icon(i, viewportWidth, showMinimap).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets the hover tooltip box: to the left of the strip, level with the hovered icon's row, so it never covers the
    /// minimap, the clock or the other icons.
    /// </summary>
    public static RectangleF Tooltip(float width, int hoveredIndex, int viewportWidth, bool showMinimap)
    {
        RectangleF icon = Icon(Math.Max(0, hoveredIndex), viewportWidth, showMinimap);
        RectangleF strip = HudLayout.GetStatusStripRect(viewportWidth, showMinimap, 1);
        float x = MathF.Max(8f, strip.X - PanelGap - width);
        return new RectangleF(x, icon.Y + ((IconSize - TooltipHeight) * 0.5f), width, TooltipHeight);
    }

    /// <summary>
    /// Gets the details panel rectangle: to the left of the strip from its top row, and below the top-centre target card
    /// when a narrow window would make them meet.
    /// </summary>
    public static RectangleF Panel(float height, int viewportWidth, bool showMinimap)
    {
        RectangleF strip = HudLayout.GetStatusStripRect(viewportWidth, showMinimap, 1);
        float x = MathF.Max(8f, strip.X - PanelGap - PanelWidth);
        float y = strip.Y;
        if (x < (viewportWidth * 0.5f) + TargetCardHalfWidth + PanelGap)
        {
            y = MathF.Max(y, TargetCardBottom + PanelGap);
        }

        return new RectangleF(x, y, PanelWidth, height);
    }
}
