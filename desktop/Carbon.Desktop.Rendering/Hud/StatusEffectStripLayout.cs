// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Screen rectangles of the top-right status strip (ADR-029 D10): one icon per active effect, right-aligned left of the
/// minimap card (or at the corner when the minimap is hidden), six per row, and the details panel under the strip.
/// </summary>
internal static class StatusEffectStripLayout
{
    /// <summary>Icon edge length in pixels.</summary>
    public const float IconSize = 34f;

    /// <summary>Horizontal gap between icons.</summary>
    public const float Gap = 6f;

    /// <summary>Icons per row before the strip wraps.</summary>
    public const int PerRow = 6;

    /// <summary>Row height: the icon plus its countdown label.</summary>
    public const float RowHeight = IconSize + 16f;

    /// <summary>Distance from the top of the screen.</summary>
    public const float Top = 14f;

    /// <summary>Details panel width.</summary>
    public const float PanelWidth = 300f;

    /// <summary>Left edge of the minimap card (see <c>MinimapRadarWidget</c>): <c>viewport − 152 − 24 − 8</c>.</summary>
    private const float MinimapCardInset = 184f;

    private const float EdgeMargin = 16f;

    /// <summary>Gets the right edge of the strip.</summary>
    public static float Right(int viewportWidth, bool showMinimap) =>
        viewportWidth - (showMinimap ? MinimapCardInset + 8f : EdgeMargin);

    /// <summary>Gets the rectangle of icon <paramref name="index"/>.</summary>
    public static RectangleF Icon(int index, int viewportWidth, bool showMinimap)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        int row = index / PerRow;
        int column = index % PerRow;
        float x = Right(viewportWidth, showMinimap) - ((column + 1) * IconSize) - (column * Gap);
        return new RectangleF(x, Top + (row * RowHeight), IconSize, IconSize);
    }

    /// <summary>Gets the index of the icon under <paramref name="point"/>, or -1.</summary>
    public static int HitTest(Vector2 point, int count, int viewportWidth, bool showMinimap)
    {
        for (int i = 0; i < count; i++)
        {
            if (Icon(i, viewportWidth, showMinimap).Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Gets the details panel rectangle under a strip of <paramref name="count"/> icons.</summary>
    public static RectangleF Panel(int count, float height, int viewportWidth, bool showMinimap)
    {
        int rows = Math.Max(1, (count + PerRow - 1) / PerRow);
        float x = MathF.Max(8f, Right(viewportWidth, showMinimap) - PanelWidth);
        return new RectangleF(x, Top + (rows * RowHeight) + 4f, PanelWidth, height);
    }
}
