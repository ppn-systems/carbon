// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Configuration;
using Carbon.Graphics.Windowing.Input;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Carbon.Graphics.Windowing.Window;

/// <summary>
/// Concrete game window implementation powered by Silk.NET.Windowing and Silk.NET.OpenGL.
/// Orchestrates native window lifecycle, input polling, and graphics device initialization.
/// </summary>
public sealed class GameWindow : IGameWindow
{
    private readonly IWindow _silkWindow;
    private GraphicsDevice? _device;
    private InputManager? _inputManager;
    private bool _disposed;

    /// <inheritdoc/>
    public IGraphicsDevice Device => _device ?? throw new InvalidOperationException("GraphicsDevice is not initialized until the Load event fires.");

    /// <inheritdoc/>
    public IInputManager Input => _inputManager ?? throw new InvalidOperationException("InputManager is not initialized until the Load event fires.");

    /// <inheritdoc/>
    public int Width => _silkWindow.Size.X;

    /// <inheritdoc/>
    public int Height => _silkWindow.Size.Y;

    /// <inheritdoc/>
    public string Title
    {
        get => _silkWindow.Title;
        set => _silkWindow.Title = value;
    }

    /// <inheritdoc/>
    public bool VSync
    {
        get => _silkWindow.VSync;
        set => _silkWindow.VSync = value;
    }

    /// <inheritdoc/>
    public bool IsClosing => _silkWindow.IsClosing;

    /// <inheritdoc/>
    public event Action<IGameWindow>? Load;

    /// <inheritdoc/>
    public event Action<IGameWindow, double>? Update;

    /// <inheritdoc/>
    public event Action<IGameWindow, double>? Render;

    /// <inheritdoc/>
    public event Action<IGameWindow, int, int>? Resize;

    /// <inheritdoc/>
    public event Action<IGameWindow>? Closing;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameWindow"/> class with custom or default settings.
    /// </summary>
    /// <param name="settings">Window configuration options.</param>
    public GameWindow(WindowSettings? settings = null)
    {
        WindowSettings config = settings ?? WindowSettings.Default;

        WindowOptions options = WindowOptions.Default;
        options.Title = config.Title;
        options.Size = new Vector2D<int>(config.Width, config.Height);
        options.VSync = config.VSync;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3));
        options.Samples = config.Samples;

        if (config.TargetFps > 0.0)
        {
            options.UpdatesPerSecond = config.TargetFps;
            options.FramesPerSecond = config.TargetFps;
        }

        options.WindowState = config.DisplayMode switch
        {
            WindowDisplayMode.Windowed => WindowState.Normal,
            WindowDisplayMode.Fullscreen => WindowState.Fullscreen,
            WindowDisplayMode.Borderless => WindowState.Maximized,
            _ => WindowState.Normal
        };

        options.WindowBorder = config.DisplayMode == WindowDisplayMode.Borderless
            ? WindowBorder.Hidden
            : (config.IsResizable ? WindowBorder.Resizable : WindowBorder.Fixed);

        _silkWindow = Silk.NET.Windowing.Window.Create(options);

        _silkWindow.Load += this.OnLoad;
        _silkWindow.Update += this.OnUpdate;
        _silkWindow.Render += this.OnRender;
        _silkWindow.Resize += this.OnResize;
        _silkWindow.Closing += this.OnClosing;
    }

    /// <inheritdoc/>
    public void Run() => _silkWindow.Run();

    /// <inheritdoc/>
    public void Close() => _silkWindow.Close();

    private void OnLoad()
    {
        GL gl = GL.GetApi(_silkWindow);
        _device = new GraphicsDevice(gl);

        Silk.NET.Input.IInputContext silkInput = _silkWindow.CreateInput();
        _inputManager = new InputManager(silkInput);

        _device.SetViewport(0, 0, (uint)Math.Max(1, _silkWindow.Size.X), (uint)Math.Max(1, _silkWindow.Size.Y));

        this.Load?.Invoke(this);
    }

    private void OnUpdate(double dt)
    {
        _inputManager?.Update();
        this.Update?.Invoke(this, dt);
    }

    private void OnRender(double dt) =>
        this.Render?.Invoke(this, dt);

    private void OnResize(Vector2D<int> size)
    {
        _device?.SetViewport(0, 0, (uint)Math.Max(1, size.X), (uint)Math.Max(1, size.Y));
        this.Resize?.Invoke(this, size.X, size.Y);
    }

    private void OnClosing()
    {
        this.Closing?.Invoke(this);
        this.Dispose();
    }

    /// <summary>
    /// Releases the native window, graphics device, and input contexts.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _silkWindow.Load -= this.OnLoad;
        _silkWindow.Update -= this.OnUpdate;
        _silkWindow.Render -= this.OnRender;
        _silkWindow.Resize -= this.OnResize;
        _silkWindow.Closing -= this.OnClosing;

        _inputManager?.Dispose();
        _device?.Dispose();
        _silkWindow.Dispose();
    }
}
