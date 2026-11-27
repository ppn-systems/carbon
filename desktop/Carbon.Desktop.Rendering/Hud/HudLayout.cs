// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Screen rectangles of the right-hand HUD column, shared by drawing, hit testing and the widgets that stack under it.
/// From the top-right corner: the minimap (when enabled), the day/night clock, the status-effect icon strip (one row per
/// <see cref="StatusEffectStripLayout.IconsPerRow"/> icons), then toasts and the skill bar below all of them.
/// </summary>
internal static class HudLayout
{
    /// <summary>Distance from the right screen edge.</summary>
    internal const float EdgeMargin = 16f;

    /// <summary>Distance from the top screen edge to the first panel of the column.</summary>
    internal const float TopMargin = 8f;

    /// <summary>Height of one row of the status-effect icon strip.</summary>
    internal const float StatusRowHeight = 40f;

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

    /// <summary>Gets the minimap frame (map, border and footer) at the top-right corner.</summary>
    internal static RectangleF GetMinimapFrameRect(int viewportWidth) =>
        new(viewportWidth - EdgeMargin - ColumnWidth, TopMargin, ColumnWidth, MapPadding + MapSize + MapFooterHeight);

    /// <summary>Gets the square map viewport inside <see cref="GetMinimapFrameRect"/>.</summary>
    internal static RectangleF GetMinimapMapRect(int viewportWidth)
    {
        RectangleF frame = GetMinimapFrameRect(viewportWidth);
        return new RectangleF(frame.X + MathF.Floor((frame.Width - MapSize) * 0.5f), frame.Y + MapPadding, MapSize, MapSize);
    }

    /// <summary>Gets the day/night clock panel: under the minimap, or at the top-right corner when the minimap is hidden.</summary>
    internal static RectangleF GetClockRect(int viewportWidth, bool showMinimap)
    {
        float top = showMinimap ? GetMinimapFrameRect(viewportWidth).Bottom + Gap : TopMargin;
        return new RectangleF(viewportWidth - EdgeMargin - ColumnWidth, top, ColumnWidth, ClockHeight);
    }

    /// <summary>Gets the bottom of the minimap/clock column.</summary>
    internal static float GetColumnBottom(int viewportWidth, bool showMinimap) => GetClockRect(viewportWidth, showMinimap).Bottom;

    /// <summary>
    /// Gets the status-effect icon strip under the column: right-aligned, no wider than the column, and
    /// <paramref name="rows"/> rows tall (zero rows when no effect is active).
    /// </summary>
    internal static RectangleF GetStatusStripRect(int viewportWidth, bool showMinimap, int rows)
    {
        float width = MathF.Min(ColumnWidth, MathF.Max(0f, viewportWidth - (EdgeMargin * 2f)));
        float top = GetColumnBottom(viewportWidth, showMinimap) + Gap;
        return new RectangleF(viewportWidth - EdgeMargin - width, top, width, Math.Max(0, rows) * StatusRowHeight);
    }

    /// <summary>Gets the bottom of everything stacked at the top right: the column and, when it has rows, the status strip.</summary>
    internal static float GetStackBottom(int viewportWidth, bool showMinimap, int statusRows) =>
        statusRows > 0 ? GetStatusStripRect(viewportWidth, showMinimap, statusRows).Bottom : GetColumnBottom(viewportWidth, showMinimap);

    /// <summary>Width of the oxygen bar (ADR-032).</summary>
    internal const float OxygenBarWidth = 220f;

    /// <summary>Height of the oxygen bar.</summary>
    internal const float OxygenBarHeight = 14f;

    /// <summary>Top of the vitals cluster (level, health, mana, stamina, food, rest) above the hotbar and XP strip.</summary>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <returns>The cluster's top edge.</returns>
    internal static float GetVitalsTop(int viewportHeight) => viewportHeight - 54f - 16f - 15f - 24f;

    /// <summary>
    /// Gets the oxygen bar (ADR-032 D13): centred just above the vitals cluster, so it reads as one of the vitals
    /// while staying clear of them, the hotbar, the status strip and the right-hand column.
    /// </summary>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <returns>The bar rectangle.</returns>
    internal static RectangleF GetOxygenBarRect(int viewportWidth, int viewportHeight) =>
        new(MathF.Floor((viewportWidth - OxygenBarWidth) * 0.5f), GetVitalsTop(viewportHeight) - Gap - OxygenBarHeight, OxygenBarWidth, OxygenBarHeight);

    /// <summary>Gets the top of the first toast, under the column and the last status-strip row.</summary>
    internal static float GetToastTop(int viewportWidth, bool showMinimap, int statusRows) =>
        GetStackBottom(viewportWidth, showMinimap, statusRows) + 8f;

    /// <summary>Gets the lowest top the right-edge skill bar may use so it never covers the column or the status strip.</summary>
    internal static float GetSkillBarMinTop(int viewportWidth, bool showMinimap, int statusRows) =>
        GetStackBottom(viewportWidth, showMinimap, statusRows) + 10f;
}
