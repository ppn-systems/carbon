// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.Assets.Abstractions;
using Carbon.Graphics.Assets.Loaders;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.Textures;

namespace Carbon.Graphics.Assets.Management;

/// <summary>
/// Centralized asset management facade providing automatic root path resolution, loading pipelines, and VRAM deduplication caching.
/// </summary>
public sealed class AssetManager : IAssetManager
{
    private readonly IAssetCache _cache;
    private readonly TextureLoader _textureLoader;
    private readonly FontLoader _fontLoader;
    private readonly AtlasLoader _atlasLoader;
    private string _rootDirectory = string.Empty;
    private bool _disposed;

    /// <inheritdoc/>
    public string RootDirectory
    {
        get => _rootDirectory;
        set => _rootDirectory = value ?? string.Empty;
    }

    /// <inheritdoc/>
    public IAssetCache Cache => _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetManager"/> class using the specified graphics device.
    /// </summary>
    /// <param name="device">The hardware graphics device.</param>
    /// <param name="rootDirectory">Optional root directory path for relative asset resolution.</param>
    /// <param name="customCache">Optional custom asset cache instance.</param>
    public AssetManager(GraphicsDevice device, string? rootDirectory = null, IAssetCache? customCache = null)
    {
        ArgumentNullException.ThrowIfNull(device, nameof(device));

        _cache = customCache ?? new AssetCache();
        _textureLoader = new TextureLoader(device);
        _fontLoader = new FontLoader(_textureLoader);
        _atlasLoader = new AtlasLoader(_textureLoader);

        if (!string.IsNullOrEmpty(rootDirectory))
        {
            this.RootDirectory = rootDirectory;
        }
    }

    /// <inheritdoc/>
    public Texture2D LoadTexture(string path, TextureFilter filter = TextureFilter.Nearest)
    {
        ArgumentException.ThrowIfNullOrEmpty(path, nameof(path));
        string resolvedPath = this.ResolvePath(path);

        if (_cache.TryGet(resolvedPath, out Texture2D? cached) && cached != null)
        {
            return cached;
        }

        Texture2D loaded = _textureLoader.LoadFromFile(resolvedPath, filter);
        _cache.Store(resolvedPath, loaded);
        return loaded;
    }

    /// <inheritdoc/>
    public BitmapFont LoadFont(string fontDescriptorPath, string? texturePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(fontDescriptorPath, nameof(fontDescriptorPath));
        string resolvedPath = this.ResolvePath(fontDescriptorPath);
        string? resolvedTexPath = !string.IsNullOrEmpty(texturePath) ? this.ResolvePath(texturePath) : null;

        if (_cache.TryGet(resolvedPath, out BitmapFont? cached) && cached != null)
        {
            return cached;
        }

        BitmapFont loaded = _fontLoader.LoadFromFile(resolvedPath, resolvedTexPath);
        _cache.Store(resolvedPath, loaded);
        return loaded;
    }

    /// <inheritdoc/>
    public SpriteAtlas LoadAtlas(string manifestPath, string? texturePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(manifestPath, nameof(manifestPath));
        string resolvedPath = this.ResolvePath(manifestPath);
        string? resolvedTexPath = !string.IsNullOrEmpty(texturePath) ? this.ResolvePath(texturePath) : null;

        if (_cache.TryGet(resolvedPath, out SpriteAtlas? cached) && cached != null)
        {
            return cached;
        }

        SpriteAtlas loaded = _atlasLoader.LoadFromFile(resolvedPath, resolvedTexPath);
        _cache.Store(resolvedPath, loaded);
        return loaded;
    }

    /// <inheritdoc/>
    public bool Unload(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path, nameof(path));
        string resolvedPath = this.ResolvePath(path);
        return _cache.Unload(resolvedPath);
    }

    /// <inheritdoc/>
    public void Clear() =>
        _cache.Clear();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cache.Dispose();
    }

    private string ResolvePath(string relativeOrAbsolutePath)
    {
        if (Path.IsPathRooted(relativeOrAbsolutePath) || string.IsNullOrEmpty(_rootDirectory))
        {
            return relativeOrAbsolutePath;
        }

        return Path.Combine(_rootDirectory, relativeOrAbsolutePath);
    }
}
