// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.Fonts.TrueType;

/// <summary>
/// One TrueType face in an ordered font stack. The first face defines line metrics; later faces only
/// supply glyphs the earlier faces lack (ADR-020 D5).
/// </summary>
public sealed class FontFaceSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FontFaceSource"/> class.
    /// </summary>
    /// <param name="name">Friendly face name.</param>
    /// <param name="data">Raw bytes of the <c>.ttf</c> file.</param>
    /// <param name="pixelSize">Pixel height the face is rasterized at.</param>
    public FontFaceSource(string name, byte[] data, float pixelSize)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentNullException.ThrowIfNull(data);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize, nameof(pixelSize));
        this.Name = name;
        this.Data = data;
        this.PixelSize = pixelSize;
    }

    /// <summary>Gets the friendly face name.</summary>
    public string Name { get; }

    /// <summary>Gets the raw TrueType bytes.</summary>
    public byte[] Data { get; }

    /// <summary>Gets the rasterization pixel height.</summary>
    public float PixelSize { get; }
}
