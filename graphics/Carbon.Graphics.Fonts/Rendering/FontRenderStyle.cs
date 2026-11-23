// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.Fonts.Rendering;

/// <summary>
/// Specifies the visual decoration style applied to rendered text.
/// </summary>
public enum FontRenderStyle
{
    /// <summary>
    /// Plain text without decoration.
    /// </summary>
    Regular = 0,

    /// <summary>
    /// Text rendered with a soft or hard drop shadow.
    /// </summary>
    DropShadow = 1,

    /// <summary>
    /// Text rendered with a solid high-contrast outline (ideal for MMORPG floating combat text and nameplates).
    /// </summary>
    Outlined = 2
}
