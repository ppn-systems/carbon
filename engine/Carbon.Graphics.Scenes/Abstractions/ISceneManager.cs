// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Scenes.Management;

namespace Carbon.Graphics.Scenes.Abstractions;

/// <summary>
/// Defines the state machine contract for orchestrating scene stack transitions, lifecycle dispatches, and modal scene nesting.
/// </summary>
public interface ISceneManager : IDisposable
{
    /// <summary>
    /// Gets the current active top-level presentation scene, or null if no scene has been mounted.
    /// </summary>
    IScene? CurrentScene { get; }

    /// <summary>
    /// Gets the total number of scenes currently residing in the active stack hierarchy.
    /// </summary>
    int SceneCount { get; }

    /// <summary>
    /// Initiates a full scene change transition, unloading the current scene hierarchy and mounting a new scene.
    /// </summary>
    /// <typeparam name="T">The concrete scene type to instantiate and mount.</typeparam>
    /// <param name="data">Optional payload data transferred into the next scene.</param>
    /// <param name="transitionDuration">Duration in seconds for fade transitions (0.0 for immediate swap).</param>
    void ChangeScene<T>(SceneTransitionData? data = null, float transitionDuration = 0f) where T : IScene, new();

    /// <summary>
    /// Initiates a full scene change transition to an existing scene instance.
    /// </summary>
    /// <param name="nextScene">The scene to mount as active.</param>
    /// <param name="data">Optional payload data transferred into the next scene.</param>
    /// <param name="transitionDuration">Duration in seconds for fade transitions (0.0 for immediate swap).</param>
    void ChangeScene(IScene nextScene, SceneTransitionData? data = null, float transitionDuration = 0f);

    /// <summary>
    /// Pushes a modal scene onto the top of the stack without unloading previous underlying scenes (e.g. Pause Menu, Inventory).
    /// </summary>
    /// <param name="modalScene">The modal overlay scene to push.</param>
    /// <param name="data">Optional payload data transferred into the modal scene.</param>
    void PushScene(IScene modalScene, SceneTransitionData? data = null);

    /// <summary>
    /// Pops the active top-level modal scene, resuming presentation on the preceding underlying scene.
    /// </summary>
    /// <returns><see langword="true"/> if a scene was popped; <see langword="false"/> if only the root scene remains.</returns>
    bool PopScene();

    /// <summary>
    /// Advances tick simulation logic for the active scene stack.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="dt">Elapsed delta time in seconds.</param>
    void Update(double dt);

    /// <summary>
    /// Renders active scenes and transition overlays to the GPU sprite batch.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="spriteBatch">The active sprite batch renderer.</param>
    /// <param name="dt">Elapsed delta time in seconds.</param>
    void Render(SpriteBatch spriteBatch, double dt);

    /// <summary>
    /// Propagates window resize events across the entire scene stack hierarchy.
    /// </summary>
    /// <param name="width">New window framebuffer width in pixels.</param>
    /// <param name="height">New window framebuffer height in pixels.</param>
    void Resize(int width, int height);
}
