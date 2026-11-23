// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.Windowing.Configuration;

/// <summary>
/// Specifies the display presentation mode of the game application window.
/// </summary>
public enum WindowDisplayMode
{
    /// <summary>
    /// Standard windowed mode with title bar and resizable borders.
    /// </summary>
    Windowed = 0,

    /// <summary>
    /// Borderless window matching the desktop resolution.
    /// </summary>
    Borderless = 1,

    /// <summary>
    /// Exclusive full screen mode.
    /// </summary>
    Fullscreen = 2
}
