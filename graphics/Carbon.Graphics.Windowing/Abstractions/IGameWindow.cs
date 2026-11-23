// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.Windowing.Abstractions;

/// <summary>
/// Defines the lifecycle, presentation loop, and device management for the native client game window.
/// </summary>
public interface IGameWindow : IDisposable
{
    /// <summary>
    /// Gets the hardware graphics device created for this window's OpenGL context.
    /// Available after <see cref="Load"/> has fired.
    /// </summary>
    IGraphicsDevice Device { get; }

    /// <summary>
    /// Gets the input manager polling keyboard, mouse, and text inputs for this window.
    /// Available after <see cref="Load"/> has fired.
    /// </summary>
    IInputManager Input { get; }

    /// <summary>
    /// Gets the current framebuffer client width in pixels.
    /// </summary>
    int Width { get; }

    /// <summary>
    /// Gets the current framebuffer client height in pixels.
    /// </summary>
    int Height { get; }

    /// <summary>
    /// Gets or sets the title bar caption text.
    /// </summary>
    string Title { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether vertical synchronization (VSync) is enabled.
    /// </summary>
    bool VSync { get; set; }

    /// <summary>
    /// Gets a value indicating whether the window is currently in the process of closing.
    /// </summary>
    bool IsClosing { get; }

    /// <summary>
    /// Occurs once when the native window and OpenGL context have been initialized.
    /// </summary>
    event Action<IGameWindow>? Load;

    /// <summary>
    /// Occurs each simulation tick to advance game logic. Parameter is elapsed delta time in seconds.
    /// </summary>
    event Action<IGameWindow, double>? Update;

    /// <summary>
    /// Occurs each frame to render graphics. Parameter is elapsed delta time in seconds.
    /// </summary>
    event Action<IGameWindow, double>? Render;

    /// <summary>
    /// Occurs when the window size changes. Parameters are new width and height in pixels.
    /// </summary>
    event Action<IGameWindow, int, int>? Resize;

    /// <summary>
    /// Occurs when the window is about to close.
    /// </summary>
    event Action<IGameWindow>? Closing;

    /// <summary>
    /// Starts the native message loop and blocks until the window is closed.
    /// </summary>
    void Run();

    /// <summary>
    /// Requests the window to close and exit its presentation loop.
    /// </summary>
    void Close();
}
