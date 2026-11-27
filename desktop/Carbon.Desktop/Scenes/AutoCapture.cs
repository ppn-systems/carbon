// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Rendering.Capture;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// The screenshot tool's schedule for the world scene (<c>tools/screenshots</c>): one capture once the world has settled
/// (later when the scene still waits for something, e.g. a trader window), then <see cref="DesktopOptions.CaptureFrames"/>
/// consecutive frames, then the window closes when <see cref="DesktopOptions.AutoExit"/> is set.
/// </summary>
/// <param name="options">Desktop options.</param>
internal sealed class AutoCapture(DesktopOptions options)
{
    /// <summary>First frame the capture may happen on.</summary>
    private const int FirstFrame = 20;

    private readonly DesktopOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private int _capturedAtFrame = -1;

    /// <summary>Runs after each rendered frame: captures, captures the frame sequence, or closes the window when done.</summary>
    /// <param name="context">Scene context.</param>
    /// <param name="frame">Frames rendered so far.</param>
    /// <param name="waiting">Whether the scene still waits for something the capture should show.</param>
    public void AfterFrame(SceneContext context, int frame, bool waiting)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_capturedAtFrame < 0)
        {
            if (frame >= FirstFrame && !waiting)
            {
                _capturedAtFrame = frame;
                Capture(context.Window, _options.ScreenshotPath);
            }

            return;
        }

        if (frame > _capturedAtFrame && frame <= _capturedAtFrame + _options.CaptureFrames)
        {
            string path = Path.Combine(
                Path.GetDirectoryName(_options.ScreenshotPath) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(_options.ScreenshotPath)}_{frame - _capturedAtFrame:D3}.bmp");
            ScreenshotHelper.Capture(context.Device.Gl, context.WindowWidth, context.WindowHeight, path);
        }
        else if (_options.AutoExit && frame >= _capturedAtFrame + 5 + _options.CaptureFrames)
        {
            context.Window.Close();
        }
    }

    /// <summary>Saves the current frame to <paramref name="filePath"/>, logging instead of throwing on I/O errors.</summary>
    /// <param name="window">Window to read back.</param>
    /// <param name="filePath">Destination file.</param>
    public static void Capture(IGameWindow window, string filePath)
    {
        ArgumentNullException.ThrowIfNull(window);
        try
        {
            ScreenshotHelper.Capture(window.Device.Gl, window.Width, window.Height, filePath);
            Console.WriteLine($"[Screenshot] Saved capture to: {filePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.WriteLine($"[Screenshot Error] {ex.Message}");
        }
    }
}
