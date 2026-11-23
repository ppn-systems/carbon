namespace Carbon.Graphics.OpenGL.Textures;

/// <summary>
/// Specifies the texture minification and magnification sampling filter.
/// </summary>
public enum TextureFilter
{
    /// <summary>
    /// Nearest-neighbor filtering (preserves sharp edges for pixel art and 2.5D retro aesthetics).
    /// </summary>
    Nearest = 0,

    /// <summary>
    /// Bilinear interpolation filtering (smoothes transitions between texture texels).
    /// </summary>
    Linear = 1
}
