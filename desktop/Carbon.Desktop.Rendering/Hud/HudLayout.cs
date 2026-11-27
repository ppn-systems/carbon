// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Screen rectangles of the right-hand HUD column, shared by drawing, hit testing and the widgets that stack under it.
/// From the top: the status-effect icon strip (reserved for the buff/debuff icons, kept clear of everything else here),
/// the day/night clock, the minimap (when enabled), then toasts and the skill bar below the column.
/// </summary>
internal static class HudLayout
{
    /// <summary>Distance from the right screen edge.</summary>
    internal const float EdgeMargin = 16f;

    /// <summary>Top of the status-effect icon strip.</summary>
    internal const float StatusStripTop = 8f;

    /// <summary>Height of the status-effect icon strip.</summary>
    internal const float StatusStripHeight = 40f;

    /// <summary>Width of the status-effect icon strip.</summary>
    internal const float StatusStripWidth = 360f;

    /// <summary>Width of the clock and minimap column.</summary>
    internal const float ColumnWidth = 196f;

    /// <summary>Height of the clock panel.</summary>
    internal const float ClockHeight = 50f;

    /// <summary>Side of the square minimap viewport.</summary>
    internal const float MapSize = 180f;

    /// <summary>Inner padding of the minimap frame.</summary>
    internal const float MapPadding = 8f;

    /// <summary>Height of the minimap footer (coordinates, zoom).</summary>
    internal const float MapFooterHeight = 20f;

    /// <summary>Vertical gap between stacked panels.</summary>
    internal const float Gap = 6f;

    /// <summary>Gets the strip reserved for status-effect icons at the top-right corner.</summary>
    internal static RectangleF GetStatusStripRect(int viewportWidth)
    {
        float width = MathF.Min(StatusStripWidth, MathF.Max(0f, viewportWidth - (EdgeMargin * 2f)));
        return new RectangleF(viewportWidth - EdgeMargin - width, StatusStripTop, width, StatusStripHeight);
    }

    /// <summary>Gets the day/night clock panel, right under the status strip.</summary>
    internal static RectangleF GetClockRect(int viewportWidth)
    {
        RectangleF strip = GetStatusStripRect(viewportWidth);
        return new RectangleF(viewportWidth - EdgeMargin - ColumnWidth, strip.Y + strip.Height + Gap, ColumnWidth, ClockHeight);
    }

    /// <summary>Gets the minimap frame (map, border and footer), right under the clock.</summary>
    internal static RectangleF GetMinimapFrameRect(int viewportWidth)
    {
        RectangleF clock = GetClockRect(viewportWidth);
        return new RectangleF(clock.X, clock.Y + clock.Height + Gap, ColumnWidth, MapPadding + MapSize + MapFooterHeight);
    }

    /// <summary>Gets the square map viewport inside <see cref="GetMinimapFrameRect"/>.</summary>
    internal static RectangleF GetMinimapMapRect(int viewportWidth)
    {
        RectangleF frame = GetMinimapFrameRect(viewportWidth);
        return new RectangleF(frame.X + MathF.Floor((frame.Width - MapSize) * 0.5f), frame.Y + MapPadding, MapSize, MapSize);
    }

    /// <summary>Gets the bottom of the clock/minimap column.</summary>
    internal static float GetColumnBottom(int viewportWidth, bool showMinimap)
    {
        RectangleF last = showMinimap ? GetMinimapFrameRect(viewportWidth) : GetClockRect(viewportWidth);
        return last.Y + last.Height;
    }

    /// <summary>Gets the top of the first toast, under the column.</summary>
    internal static float GetToastTop(int viewportWidth, bool showMinimap) => GetColumnBottom(viewportWidth, showMinimap) + 8f;

    /// <summary>Gets the lowest top the right-edge skill bar may use so it never covers the column.</summary>
    internal static float GetSkillBarMinTop(int viewportWidth, bool showMinimap) => GetColumnBottom(viewportWidth, showMinimap) + 10f;
}
