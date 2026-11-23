// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Graphics.OpenGL.Textures;

/// <summary>
/// Specifies how texture coordinates outside the [0, 1] range are sampled.
/// </summary>
public enum TextureWrap
{
    /// <summary>
    /// Clamps texture coordinates to the [0, 1] edge, preventing border bleeding.
    /// </summary>
    ClampToEdge = 0,

    /// <summary>
    /// Repeats the texture pattern infinitely (useful for procedural tiles and repeating ground textures).
    /// </summary>
    Repeat = 1,

    /// <summary>
    /// Repeats the texture pattern while mirroring every alternate tile.
    /// </summary>
    MirroredRepeat = 2
}
