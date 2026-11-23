// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.UI.Common;

namespace Carbon.Graphics.UI.Layout;

/// <summary>
/// Provides high-throughput layout calculation utilities for arranging UI elements into rows, columns, and inventory grids.
/// Zero heap allocation.
/// </summary>
public static class LayoutEngine
{
    /// <summary>
    /// Calculates the screen bounds of a cell within a uniform 2D grid (e.g. inventory slots, skill bars).
    /// </summary>
    /// <param name="container">The container rectangle enclosing the grid.</param>
    /// <param name="slotWidth">Width of each individual slot in pixels.</param>
    /// <param name="slotHeight">Height of each individual slot in pixels.</param>
    /// <param name="spacing">Spacing in pixels between adjacent slots.</param>
    /// <param name="padding">Internal padding around container border.</param>
    /// <param name="col">Column index (0-based).</param>
    /// <param name="row">Row index (0-based).</param>
    /// <returns>The calculated <see cref="RectangleF"/> bounds for the requested cell.</returns>
    public static RectangleF CalculateGridSlotBounds(
        in RectangleF container,
        float slotWidth,
        float slotHeight,
        float spacing,
        in Padding padding,
        int col,
        int row)
    {
        float startX = container.X + padding.Left;
        float startY = container.Y + padding.Top;

        float x = startX + (col * (slotWidth + spacing));
        float y = startY + (row * (slotHeight + spacing));

        return new RectangleF(x, y, slotWidth, slotHeight);
    }

    /// <summary>
    /// Calculates the horizontal distribution for a row of controls (e.g. dialog buttons [OK] [Cancel]).
    /// </summary>
    /// <param name="container">The container rectangle.</param>
    /// <param name="itemWidth">Width of each control.</param>
    /// <param name="itemHeight">Height of each control.</param>
    /// <param name="itemCount">Total number of items.</param>
    /// <param name="index">Current item index (0-based).</param>
    /// <param name="spacing">Spacing between items.</param>
    /// <returns>The calculated item bounds.</returns>
    public static RectangleF CalculateCenteredRowItemBounds(
        in RectangleF container,
        float itemWidth,
        float itemHeight,
        int itemCount,
        int index,
        float spacing)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemCount, nameof(itemCount));

        float totalWidth = (itemCount * itemWidth) + ((itemCount - 1) * spacing);
        float startX = container.X + ((container.Width - totalWidth) * 0.5f);
        float y = container.Y + ((container.Height - itemHeight) * 0.5f);
        float x = startX + (index * (itemWidth + spacing));

        return new RectangleF(x, y, itemWidth, itemHeight);
    }
}
