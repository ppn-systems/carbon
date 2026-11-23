// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.OpenGL.Textures;

/// <summary>
/// Specifies the pixel storage format for GPU textures.
/// </summary>
public enum TextureFormat
{
    /// <summary>
    /// 32-bit color format with 8 bits per RGBA component.
    /// </summary>
    Rgba8 = 0,

    /// <summary>
    /// 24-bit color format with 8 bits per RGB component.
    /// </summary>
    Rgb8 = 1,

    /// <summary>
    /// Single-channel 8-bit format (common for grayscale masks, alpha, and glyph font atlases).
    /// </summary>
    R8 = 2,

    /// <summary>
    /// Combined 24-bit depth and 8-bit stencil buffer format.
    /// </summary>
    Depth24Stencil8 = 3
}
