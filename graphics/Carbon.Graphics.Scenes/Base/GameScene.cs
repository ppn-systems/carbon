// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Scenes.Abstractions;
using Carbon.Graphics.Scenes.Management;

namespace Carbon.Graphics.Scenes.Base;

/// <summary>
/// Base class for game scenes: a name, the loaded flag and virtual lifecycle hooks that <see cref="SceneManager"/> calls.
/// Scenes draw themselves in <see cref="Render"/>.
/// </summary>
public abstract class GameScene : IScene
{
    private bool _disposed;

    /// <inheritdoc/>
    public string Name { get; protected set; }

    /// <inheritdoc/>
    public bool IsLoaded { get; protected set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GameScene"/> class with an optional name.
    /// </summary>
    /// <param name="name">The scene name identifier.</param>
    protected GameScene(string? name = null) =>
        this.Name = name ?? this.GetType().Name;

    /// <inheritdoc/>
    public virtual void Load(SceneContext context) =>
        this.IsLoaded = true;

    /// <inheritdoc/>
    public virtual void Enter(SceneContext context)
    {
    }

    /// <inheritdoc/>
    public virtual void Update(SceneContext context, double dt)
    {
    }

    /// <inheritdoc/>
    public virtual void Render(SceneContext context, SpriteBatch spriteBatch, double dt)
    {
        ArgumentNullException.ThrowIfNull(context, nameof(context));
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));
    }

    /// <inheritdoc/>
    public virtual void Leave(SceneContext context)
    {
    }

    /// <inheritdoc/>
    public virtual void Unload(SceneContext context) =>
        this.IsLoaded = false;

    /// <inheritdoc/>
    public virtual void Resize(SceneContext context, int width, int height)
    {
    }

    /// <summary>
    /// Releases the scene.
    /// </summary>
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed and unmanaged resources.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> to release both managed and unmanaged resources; <see langword="false"/> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
