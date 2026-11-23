// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Textures;

namespace Carbon.Graphics.Assets.Abstractions;

/// <summary>
/// Defines the centralized contract for reading, decoding, uploading, and caching GPU assets.
/// </summary>
public interface IAssetManager : IDisposable
{
    /// <summary>
    /// Gets or sets the root base directory used to resolve relative asset paths.
    /// </summary>
    string RootDirectory { get; set; }

    /// <summary>
    /// Gets the underlying VRAM cache instance.
    /// </summary>
    IAssetCache Cache { get; }

    /// <summary>
    /// Loads or retrieves a cached 2D texture from the specified image file (PNG, QOI, BMP, JPEG).
    /// Multiple requests for the same path return the identical VRAM instance without redundant allocations.
    /// </summary>
    /// <param name="path">The relative or absolute file path.</param>
    /// <param name="filter">Sampling filter mode (Nearest for pixel art, Linear for smooth).</param>
    /// <returns>A ready-to-render <see cref="Texture2D"/> instance.</returns>
    Texture2D LoadTexture(string path, TextureFilter filter = TextureFilter.Nearest);

    /// <summary>
    /// Loads or retrieves a cached AngelCode bitmap font (.fnt) and its associated texture atlas.
    /// </summary>
    /// <param name="fontDescriptorPath">Path to the .fnt descriptor file.</param>
    /// <param name="texturePath">Optional explicit path to the font atlas image (inferred if omitted).</param>
    /// <returns>A ready-to-render <see cref="BitmapFont"/> instance.</returns>
    BitmapFont LoadFont(string fontDescriptorPath, string? texturePath = null);

    /// <summary>
    /// Loads or retrieves a cached sprite atlas and its underlying texture.
    /// </summary>
    /// <param name="manifestPath">Path to the atlas JSON or manifest file.</param>
    /// <param name="texturePath">Optional explicit path to the atlas texture image (inferred if omitted).</param>
    /// <returns>A ready-to-render <see cref="SpriteAtlas"/> instance.</returns>
    SpriteAtlas LoadAtlas(string manifestPath, string? texturePath = null);

    /// <summary>
    /// Unloads and disposes a specific asset by its relative or absolute path.
    /// </summary>
    /// <param name="path">The asset path to unload.</param>
    /// <returns><see langword="true"/> if found and released; otherwise, <see langword="false"/>.</returns>
    bool Unload(string path);

    /// <summary>
    /// Unloads and disposes all assets currently held in the cache.
    /// </summary>
    void Clear();
}
