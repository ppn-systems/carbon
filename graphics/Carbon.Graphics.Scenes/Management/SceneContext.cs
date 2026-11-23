// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.Scenes.Abstractions;
using Carbon.Graphics.Windowing.Abstractions;

namespace Carbon.Graphics.Scenes.Management;

/// <summary>
/// Provides ambient context, hardware devices, input managers, camera matrices, and state machine controls to scenes.
/// Eliminates dependency on static global singletons.
/// </summary>
public sealed class SceneContext
{
    /// <summary>
    /// Gets the native game window wrapping lifecycle and presentation.
    /// </summary>
    public IGameWindow Window { get; }

    /// <summary>
    /// Gets the active OpenGL graphics device.
    /// </summary>
    public IGraphicsDevice Device => this.Window.Device;

    /// <summary>
    /// Gets the hardware input manager polling keyboard, mouse, and character text events.
    /// </summary>
    public IInputManager Input => this.Window.Input;

    /// <summary>
    /// Gets the primary 2D world camera controlling viewports and coordinate transformations.
    /// </summary>
    public Camera2D Camera { get; }

    /// <summary>
    /// Gets the scene manager orchestrating transitions and modal stacking.
    /// </summary>
    public ISceneManager SceneManager { get; }

    /// <summary>
    /// Gets the payload data passed from the preceding scene during transition, or null.
    /// </summary>
    public SceneTransitionData? TransitionData { get; internal set; }

    /// <summary>
    /// Gets the current window framebuffer client width in pixels.
    /// </summary>
    public int WindowWidth => this.Window.Width;

    /// <summary>
    /// Gets the current window framebuffer client height in pixels.
    /// </summary>
    public int WindowHeight => this.Window.Height;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneContext"/> class.
    /// </summary>
    /// <param name="window">The client game window.</param>
    /// <param name="camera">The 2D world camera.</param>
    /// <param name="sceneManager">The scene state machine manager.</param>
    public SceneContext(IGameWindow window, Camera2D camera, ISceneManager sceneManager)
    {
        this.Window = window ?? throw new ArgumentNullException(nameof(window));
        this.Camera = camera ?? throw new ArgumentNullException(nameof(camera));
        this.SceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
    }
}
