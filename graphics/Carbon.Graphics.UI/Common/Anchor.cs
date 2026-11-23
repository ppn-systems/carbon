// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.UI.Common;

/// <summary>
/// Specifies screen or container alignment anchors for UI elements.
/// </summary>
public enum Anchor
{
    /// <summary>
    /// Anchors to the top-left corner.
    /// </summary>
    TopLeft = 0,

    /// <summary>
    /// Anchors to the top-center edge.
    /// </summary>
    TopCenter = 1,

    /// <summary>
    /// Anchors to the top-right corner.
    /// </summary>
    TopRight = 2,

    /// <summary>
    /// Anchors to the middle-left edge.
    /// </summary>
    CenterLeft = 3,

    /// <summary>
    /// Anchors to the exact center.
    /// </summary>
    Center = 4,

    /// <summary>
    /// Anchors to the middle-right edge.
    /// </summary>
    CenterRight = 5,

    /// <summary>
    /// Anchors to the bottom-left corner.
    /// </summary>
    BottomLeft = 6,

    /// <summary>
    /// Anchors to the bottom-center edge.
    /// </summary>
    BottomCenter = 7,

    /// <summary>
    /// Anchors to the bottom-right corner.
    /// </summary>
    BottomRight = 8,

    /// <summary>
    /// Stretches to fill the entire container boundaries.
    /// </summary>
    Stretch = 9
}
