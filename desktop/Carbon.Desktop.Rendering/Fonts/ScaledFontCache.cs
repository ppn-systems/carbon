// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Fonts;

/// <summary>
/// Holds the UI font baked for the current UI scale and re-bakes it only when the scale changes (a window resize or a new
/// Settings choice), never per frame.
/// </summary>
public sealed class ScaledFontCache : IDisposable
{
    private readonly Func<float, BitmapFont> _bake;
    private BitmapFont? _font;

    /// <summary>Initializes a new instance of the <see cref="ScaledFontCache"/> class.</summary>
    /// <param name="bake">Bakes the font for a UI scale (screen pixels per layout unit).</param>
    public ScaledFontCache(Func<float, BitmapFont> bake) => _bake = bake ?? throw new ArgumentNullException(nameof(bake));

    /// <summary>Gets the font baked last, or <see langword="null"/> before the first <see cref="Get"/>.</summary>
    public BitmapFont? Font => _font;

    /// <summary>Gets the scale the current font was baked for.</summary>
    public float Scale { get; private set; }

    /// <summary>Gets how many times a font has been baked.</summary>
    public int BakeCount { get; private set; }

    /// <summary>Returns the font for <paramref name="scale"/>, baking a new one only when the scale differs from the last.</summary>
    /// <param name="scale">Screen pixels per layout unit.</param>
    /// <returns>The font.</returns>
    public BitmapFont Get(float scale)
    {
        if (_font is { } current && scale == this.Scale)
        {
            return current;
        }

        BitmapFont baked = _bake(scale);
        _font?.Dispose();
        _font = baked;
        this.Scale = scale;
        this.BakeCount++;
        return baked;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _font?.Dispose();
        _font = null;
    }
}
