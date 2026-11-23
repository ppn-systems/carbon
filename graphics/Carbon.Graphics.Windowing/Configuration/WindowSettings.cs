// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.Windowing.Configuration;

/// <summary>
/// Defines creation and presentation settings for configuring the native game window.
/// </summary>
public sealed class WindowSettings
{
    /// <summary>
    /// Gets or sets the window title bar caption.
    /// </summary>
    public string Title { get; set; } = "Carbon Engine";

    /// <summary>
    /// Gets or sets the horizontal resolution width in pixels.
    /// </summary>
    public int Width { get; set; } = 1280;

    /// <summary>
    /// Gets or sets the vertical resolution height in pixels.
    /// </summary>
    public int Height { get; set; } = 720;

    /// <summary>
    /// Gets or sets a value indicating whether vertical synchronization (VSync) is enabled to prevent screen tearing.
    /// </summary>
    public bool VSync { get; set; } = true;

    /// <summary>
    /// Gets or sets the window display mode (Windowed, Borderless, Fullscreen).
    /// </summary>
    public WindowDisplayMode DisplayMode { get; set; } = WindowDisplayMode.Windowed;

    /// <summary>
    /// Gets or sets a value indicating whether the user is permitted to resize the window.
    /// </summary>
    public bool IsResizable { get; set; } = true;

    /// <summary>
    /// Gets or sets the target update rate in frames per second (0.0 for unlimited).
    /// </summary>
    public double TargetFps { get; set; }

    /// <summary>
    /// Gets or sets the multi-sample anti-aliasing (MSAA) sample count (0 for none).
    /// </summary>
    public int Samples { get; set; }

    /// <summary>
    /// Gets default standard 720p window configuration settings.
    /// </summary>
    public static WindowSettings Default => new();
}
