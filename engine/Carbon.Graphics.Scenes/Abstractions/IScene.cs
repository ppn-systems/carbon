// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Scenes.Management;

namespace Carbon.Graphics.Scenes.Abstractions;

/// <summary>
/// Defines the lifecycle, simulation loop, and rendering pipeline contract for a distinct game scene.
/// </summary>
public interface IScene : IDisposable
{
    /// <summary>
    /// Gets the human-readable identifier name for this scene.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets a value indicating whether scene assets and objects have been loaded.
    /// </summary>
    bool IsLoaded { get; }

    /// <summary>
    /// Loads scene-specific assets, builds initial object rosters, and prepares simulation state.
    /// </summary>
    /// <param name="context">The ambient scene environment context.</param>
    void Load(SceneContext context);

    /// <summary>
    /// Called when the scene becomes the active top-level presentation scene.
    /// </summary>
    /// <param name="context">The ambient scene environment context.</param>
    void Enter(SceneContext context);

    /// <summary>
    /// Advances simulation logic, object behaviors, and transitions during each frame tick.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="context">The ambient scene environment context.</param>
    /// <param name="dt">Elapsed delta time in seconds.</param>
    void Update(SceneContext context, double dt);

    /// <summary>
    /// Dispatches scene visual objects and interfaces to the active GPU sprite batch.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="context">The ambient scene environment context.</param>
    /// <param name="spriteBatch">The active sprite batch renderer.</param>
    /// <param name="dt">Elapsed delta time in seconds.</param>
    void Render(SceneContext context, SpriteBatch spriteBatch, double dt);

    /// <summary>
    /// Called when the scene is leaving the active top-level presentation state.
    /// </summary>
    /// <param name="context">The ambient scene environment context.</param>
    void Leave(SceneContext context);

    /// <summary>
    /// Releases scene-specific loaded assets and clears allocated objects.
    /// </summary>
    /// <param name="context">The ambient scene environment context.</param>
    void Unload(SceneContext context);

    /// <summary>
    /// Occurs when the presentation window client area is resized.
    /// </summary>
    /// <param name="context">The ambient scene environment context.</param>
    /// <param name="width">New client width in pixels.</param>
    /// <param name="height">New client height in pixels.</param>
    void Resize(SceneContext context, int width, int height);
}
