// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Rendering.Capture;
using Carbon.Graphics.Scenes.Management;

namespace Carbon.Desktop.Capture;

/// <summary>
/// Headless capture of a menu scene for UI review (<c>CARBON_CAPTURE_SCENE=login|characters</c>, ADR-023 D8):
/// once the scene is ready it renders a few frames, saves <see cref="DesktopOptions.ScreenshotPath"/>, then closes
/// the window when <see cref="DesktopOptions.AutoExit"/> is set.
/// </summary>
internal sealed class MenuSceneCapture
{
    private const int CaptureFrame = 20;
    private readonly DesktopOptions _options;
    private readonly bool _enabled;
    private int _readyFrames;
    private bool _captured;

    /// <summary>Initializes a new instance of the <see cref="MenuSceneCapture"/> class.</summary>
    /// <param name="options">Desktop options.</param>
    /// <param name="sceneName">The <c>CARBON_CAPTURE_SCENE</c> value this scene answers to.</param>
    public MenuSceneCapture(DesktopOptions options, string sceneName)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _enabled = string.Equals(options.CaptureScene, sceneName, StringComparison.Ordinal);
    }

    /// <summary>Gets whether this scene is the capture target.</summary>
    public bool Enabled => _enabled;

    /// <summary>Call at the end of <c>Render</c>, after everything is drawn.</summary>
    /// <param name="context">The scene context.</param>
    /// <param name="ready">Whether the scene shows its final content (e.g. roster loaded).</param>
    public void AfterRender(SceneContext context, bool ready)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_enabled || !ready)
        {
            return;
        }

        _readyFrames++;
        if (!_captured && _readyFrames >= CaptureFrame)
        {
            _captured = true;
            try
            {
                ScreenshotHelper.Capture(context.Window.Device.Gl, context.Window.Width, context.Window.Height, _options.ScreenshotPath);
                Console.WriteLine($"[Screenshot] Saved capture to: {_options.ScreenshotPath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Console.WriteLine($"[Screenshot Error] {ex.Message}");
            }
        }

        if (_captured && _options.AutoExit && _readyFrames >= CaptureFrame + 4)
        {
            context.Window.Close();
        }
    }
}
