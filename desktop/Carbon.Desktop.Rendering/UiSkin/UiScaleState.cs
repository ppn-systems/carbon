// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.Fonts;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.UiSkin;

/// <summary>
/// The one UI scale the menus and the in-game HUD share (docs/art/menu-ui.md rule 21): the player's choice, the scale it
/// resolves to for the window, the canvas that leaves, and the single <see cref="ScaledFontCache"/> both draw with, so
/// moving between the menus and the world never re-bakes the font.
/// </summary>
public sealed class UiScaleState : IDisposable
{
    private readonly Func<float?> _readChoice;
    private ScaledFontCache? _fonts;

    /// <summary>Initializes a new instance of the <see cref="UiScaleState"/> class.</summary>
    /// <param name="readChoice">Reads the saved choice (<see langword="null"/> = Auto) from <c>settings.ini</c>.</param>
    public UiScaleState(Func<float?> readChoice) => _readChoice = readChoice ?? throw new ArgumentNullException(nameof(readChoice));

    /// <summary>Gets or sets the player's choice (<see langword="null"/> = Auto); Settings sets it when the player picks one.</summary>
    public float? Choice { get; set; }

    /// <summary>Gets the screen pixels per canvas unit for the window last passed to <see cref="Resize"/>.</summary>
    public float Scale { get; private set; } = 1f;

    /// <summary>Gets the window height last passed to <see cref="Resize"/> (callers that only know the width reuse it).</summary>
    public int WindowHeight { get; private set; }

    /// <summary>Gets the canvas width in layout units (the window width divided by <see cref="Scale"/>).</summary>
    public int CanvasWidth { get; private set; }

    /// <summary>Gets the canvas height in layout units (the window height divided by <see cref="Scale"/>).</summary>
    public int CanvasHeight { get; private set; }

    /// <summary>Gets the shared font cache, or <see langword="null"/> before the first screen supplies one.</summary>
    public ScaledFontCache? Fonts => _fonts;

    /// <summary>Gets the font for the current <see cref="Scale"/>, baking it only when the scale changed; <see langword="null"/> without fonts.</summary>
    public BitmapFont? Font => _fonts?.Get(this.Scale);

    /// <summary>Re-reads the saved choice (a menu screen does this once when it loads, never per frame).</summary>
    public void ReloadChoice() => this.Choice = _readChoice();

    /// <summary>Supplies the font cache the first time; later calls keep the one already shared.</summary>
    /// <param name="create">Creates the cache (with the device's font loader).</param>
    public void EnsureFonts(Func<ScaledFontCache> create)
    {
        ArgumentNullException.ThrowIfNull(create);
        _fonts ??= create();
    }

    /// <summary>Resolves the scale and canvas for a <paramref name="windowWidth"/> × <paramref name="windowHeight"/> window.</summary>
    /// <param name="windowWidth">Window width in pixels.</param>
    /// <param name="windowHeight">Window height in pixels.</param>
    public void Resize(int windowWidth, int windowHeight)
    {
        float scale = UiScale.Resolve(this.Choice, windowWidth, windowHeight);
        this.WindowHeight = windowHeight;
        this.Scale = scale;
        this.CanvasWidth = (int)MathF.Floor(windowWidth / scale);
        this.CanvasHeight = (int)MathF.Floor(windowHeight / scale);
    }

    /// <summary>Maps a point from window pixels to canvas units.</summary>
    /// <param name="windowPoint">The point in window pixels.</param>
    /// <returns>The point in canvas units.</returns>
    public Vector2 ToCanvas(Vector2 windowPoint) => windowPoint / this.Scale;

    /// <summary>Maps a point from canvas units to window pixels.</summary>
    /// <param name="canvasPoint">The point in canvas units.</param>
    /// <returns>The point in window pixels.</returns>
    public Vector2 ToWindow(Vector2 canvasPoint) => canvasPoint * this.Scale;

    /// <inheritdoc/>
    public void Dispose()
    {
        _fonts?.Dispose();
        _fonts = null;
    }
}
