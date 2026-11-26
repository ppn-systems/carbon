// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Graphics.Scenes.Abstractions;
using Carbon.Graphics.Scenes.Transitions;
using Carbon.Graphics.Windowing.Abstractions;

namespace Carbon.Graphics.Scenes.Management;

/// <summary>
/// Orchestrates the active scene hierarchy, modal stack, transition sequencing, and frame dispatches.
/// Zero heap allocation during normal frame simulation and rendering.
/// </summary>
public sealed class SceneManager : ISceneManager
{
    private readonly List<IScene> _sceneStack = [];
    private readonly SceneTransition _transition = new();
    private readonly SceneContext _context;
    private Texture2D? _whitePixel;
    private bool _disposed;

    /// <inheritdoc/>
    public IScene? CurrentScene => _sceneStack.Count > 0 ? _sceneStack[^1] : null;

    /// <inheritdoc/>
    public int SceneCount => _sceneStack.Count;

    /// <summary>
    /// Gets the shared ambient scene context.
    /// </summary>
    public SceneContext Context => _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneManager"/> class.
    /// </summary>
    /// <param name="window">The client game window.</param>
    /// <param name="camera">The primary 2D world camera.</param>
    public SceneManager(IGameWindow window, Camera2D camera)
    {
        ArgumentNullException.ThrowIfNull(window, nameof(window));
        ArgumentNullException.ThrowIfNull(camera, nameof(camera));

        _context = new SceneContext(window, camera, this);
    }

    /// <inheritdoc/>
    public void ChangeScene<T>(SceneTransitionData? data = null, float transitionDuration = 0f) where T : IScene, new() =>
        this.ChangeScene(new T(), data, transitionDuration);

    /// <inheritdoc/>
    public void ChangeScene(IScene nextScene, SceneTransitionData? data = null, float transitionDuration = 0f)
    {
        ArgumentNullException.ThrowIfNull(nextScene, nameof(nextScene));

        if (transitionDuration <= 0f)
        {
            this.PerformSwap(nextScene, data);
        }
        else
        {
            _transition.Start(
                SceneTransitionEffect.Fade,
                transitionDuration,
                ColorRgba.Black,
                () => this.PerformSwap(nextScene, data));
        }
    }

    /// <inheritdoc/>
    public void PushScene(IScene modalScene, SceneTransitionData? data = null)
    {
        ArgumentNullException.ThrowIfNull(modalScene, nameof(modalScene));

        _context.TransitionData = data;

        if (!modalScene.IsLoaded)
        {
            modalScene.Load(_context);
        }

        _sceneStack.Add(modalScene);
        modalScene.Enter(_context);
    }

    /// <inheritdoc/>
    public bool PopScene()
    {
        if (_sceneStack.Count <= 1)
        {
            return false;
        }

        int topIndex = _sceneStack.Count - 1;
        IScene top = _sceneStack[topIndex];
        _sceneStack.RemoveAt(topIndex);

        top.Leave(_context);
        top.Unload(_context);
        top.Dispose();

        if (_sceneStack.Count > 0)
        {
            _sceneStack[^1].Enter(_context);
        }

        return true;
    }

    /// <inheritdoc/>
    public void Update(double dt)
    {
        _transition.Update(dt);

        // Advance simulation on the top-level scene
        if (_sceneStack.Count > 0)
        {
            _sceneStack[^1].Update(_context, dt);
        }
    }

    /// <inheritdoc/>
    public void Render(SpriteBatch spriteBatch, double dt)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));

        // Render stack from bottom to top so modal overlays sit properly over underlying scenes
        for (int i = 0; i < _sceneStack.Count; i++)
        {
            _sceneStack[i].Render(_context, spriteBatch, dt);
        }

        if (_transition.IsActive && _transition.Alpha > 0.001f)
        {
            int width = _context.WindowWidth;
            int height = _context.WindowHeight;
            if (width > 0 && height > 0)
            {
                this.EnsureWhitePixel();
                if (_whitePixel is not null)
                {
                    Matrix4x4 proj = Matrix4x4.CreateOrthographicOffCenter(0f, width, height, 0f, -1f, 1f);
                    RectangleF screenBounds = new(0f, 0f, width, height);
                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
                    _transition.Render(spriteBatch, _whitePixel, screenBounds);
                    spriteBatch.End();
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Resize(int width, int height)
    {
        for (int i = 0; i < _sceneStack.Count; i++)
        {
            _sceneStack[i].Resize(_context, width, height);
        }
    }

    private void PerformSwap(IScene nextScene, SceneTransitionData? data)
    {
        for (int i = _sceneStack.Count - 1; i >= 0; i--)
        {
            IScene scene = _sceneStack[i];
            scene.Leave(_context);
            scene.Unload(_context);
            scene.Dispose();
        }

        _sceneStack.Clear();

        _context.TransitionData = data;

        if (!nextScene.IsLoaded)
        {
            nextScene.Load(_context);
        }

        _sceneStack.Add(nextScene);
        nextScene.Enter(_context);
    }

    private void EnsureWhitePixel()
    {
        if (_whitePixel != null)
        {
            return;
        }

        if (_context.Device is GraphicsDevice concreteDevice)
        {
            _whitePixel = new Texture2D(concreteDevice.Gl, concreteDevice.StateCache, 1, 1, TextureFormat.Rgba8);
            _whitePixel.SetData([255, 255, 255, 255]);
        }
    }

    /// <summary>
    /// Unloads and disposes all mounted scenes and transition resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        for (int i = _sceneStack.Count - 1; i >= 0; i--)
        {
            IScene scene = _sceneStack[i];
            scene.Leave(_context);
            scene.Unload(_context);
            scene.Dispose();
        }

        _sceneStack.Clear();

        _whitePixel?.Dispose();
        _whitePixel = null;
    }
}
