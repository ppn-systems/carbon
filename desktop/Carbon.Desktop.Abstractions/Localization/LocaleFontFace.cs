// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>One face of a locale's font stack.</summary>
public sealed class LocaleFontFace
{
    /// <summary>Gets or sets the font file name under <c>resources/fonts</c>.</summary>
    public string File { get; set; } = string.Empty;

    /// <summary>Gets or sets the rasterization pixel height.</summary>
    public float PixelSize { get; set; } = 16f;
}
