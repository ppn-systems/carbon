// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Scenes.Abstractions;
using Carbon.Graphics.Scenes.Management;

namespace Carbon.Graphics.Scenes.Base;

/// <summary>
/// Foundation base class for game scenes providing integrated visual object rosters, double-buffered lifecycle queues,
/// camera frustum culling, and automated 2.5D Y-sorted GPU batch rendering.
/// </summary>
public abstract class GameScene : IScene
{
    private readonly List<IRenderObject> _activeObjects = [];
    private readonly List<IRenderObject> _pendingAdd = [];
    private readonly List<IRenderObject> _pendingRemove = [];
    private bool _disposed;

    /// <inheritdoc/>
    public string Name { get; protected set; }

    /// <inheritdoc/>
    public bool IsLoaded { get; protected set; }

    /// <summary>
    /// Gets the read-only list of active simulation and render objects currently mounted in this scene.
    /// </summary>
    public IReadOnlyList<IRenderObject> Objects => _activeObjects;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameScene"/> class with an optional name.
    /// </summary>
    /// <param name="name">The scene name identifier.</param>
    protected GameScene(string? name = null) =>
        this.Name = name ?? this.GetType().Name;

    /// <summary>
    /// Enqueues a render object to be added to this scene at the beginning of the next simulation tick.
    /// </summary>
    /// <param name="obj">The render object to mount.</param>
    public void Add(IRenderObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        _pendingAdd.Add(obj);
    }

    /// <summary>
    /// Enqueues a render object to be removed from this scene at the beginning of the next simulation tick.
    /// </summary>
    /// <param name="obj">The render object to unmount.</param>
    public void Remove(IRenderObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));
        _pendingRemove.Add(obj);
    }

    /// <summary>
    /// Clears all active and pending objects from this scene.
    /// </summary>
    public void Clear()
    {
        _pendingAdd.Clear();
        _pendingRemove.Clear();
        _activeObjects.Clear();
    }

    /// <summary>
    /// Searches active objects for the first instance matching the specified tag.
    /// </summary>
    /// <typeparam name="T">The concrete object type.</typeparam>
    /// <param name="tag">The tag to locate.</param>
    /// <returns>The found object, or null if none match.</returns>
    public T? FindFirstByTag<T>(string tag) where T : class, IRenderObject
    {
        ArgumentException.ThrowIfNullOrEmpty(tag, nameof(tag));

        for (int i = 0; i < _activeObjects.Count; i++)
        {
            if (_activeObjects[i] is T typed && string.Equals(typed.Tag, tag, StringComparison.Ordinal))
            {
                return typed;
            }
        }

        return null;
    }

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
        this.FlushPendingQueues();

        for (int i = 0; i < _activeObjects.Count; i++)
        {
            IRenderObject obj = _activeObjects[i];
            if (obj.IsEnabled)
            {
                obj.Update(dt);
            }
        }
    }

    /// <inheritdoc/>
    public virtual void Render(SceneContext context, SpriteBatch spriteBatch, double dt)
    {
        ArgumentNullException.ThrowIfNull(context, nameof(context));
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));

        if (_activeObjects.Count == 0)
        {
            return;
        }

        spriteBatch.Begin(DepthSortMode.YSorted, transformMatrix: context.Camera.ViewProjectionMatrix);

        RectangleF visibleBounds = context.Camera.GetVisibleBounds();

        for (int i = 0; i < _activeObjects.Count; i++)
        {
            IRenderObject obj = _activeObjects[i];
            if (obj.IsVisible && (visibleBounds.Width <= 0f || visibleBounds.Intersects(obj.Bounds)))
            {
                obj.Draw(spriteBatch);
            }
        }

        spriteBatch.End();
    }

    /// <inheritdoc/>
    public virtual void Leave(SceneContext context)
    {
    }

    /// <inheritdoc/>
    public virtual void Unload(SceneContext context)
    {
        this.Clear();
        this.IsLoaded = false;
    }

    /// <inheritdoc/>
    public virtual void Resize(SceneContext context, int width, int height)
    {
    }

    private void FlushPendingQueues()
    {
        if (_pendingRemove.Count > 0)
        {
            if (_pendingRemove.Count == 1)
            {
                _ = _activeObjects.Remove(_pendingRemove[0]);
            }
            else
            {
                HashSet<IRenderObject> removeSet = [.. _pendingRemove];
                _ = _activeObjects.RemoveAll(removeSet.Contains);
            }

            _pendingRemove.Clear();
        }

        if (_pendingAdd.Count > 0)
        {
            _activeObjects.AddRange(_pendingAdd);
            _pendingAdd.Clear();
        }
    }

    /// <summary>
    /// Releases scene objects and internal resources.
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

        if (disposing)
        {
            this.Clear();
        }

        _disposed = true;
    }
}
