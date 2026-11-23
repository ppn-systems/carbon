// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Textures;

namespace Carbon.Graphics.Assets.Loaders;

/// <summary>
/// Loads AngelCode BMFont (.fnt) descriptor files and coordinates automatic atlas texture loading and caching.
/// </summary>
public sealed class FontLoader
{
    private readonly TextureLoader _textureLoader;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontLoader"/> class.
    /// </summary>
    /// <param name="textureLoader">The underlying texture loader.</param>
    public FontLoader(TextureLoader textureLoader) =>
        _textureLoader = textureLoader ?? throw new ArgumentNullException(nameof(textureLoader));

    /// <summary>
    /// Loads a bitmap font from an AngelCode .fnt file descriptor and its companion texture.
    /// </summary>
    /// <param name="descriptorPath">Path to the .fnt file.</param>
    /// <param name="texturePath">Optional explicit path to the font atlas image. If omitted, resolved automatically.</param>
    /// <param name="filter">Texture sampling filter (default Linear for smooth text).</param>
    /// <returns>A populated, ready-to-render <see cref="BitmapFont"/> instance.</returns>
    public BitmapFont LoadFromFile(string descriptorPath, string? texturePath = null, TextureFilter filter = TextureFilter.Linear)
    {
        ArgumentException.ThrowIfNullOrEmpty(descriptorPath, nameof(descriptorPath));

        if (!File.Exists(descriptorPath))
        {
            throw new FileNotFoundException($"Font descriptor file not found: {descriptorPath}", descriptorPath);
        }

        string fntContent = File.ReadAllText(descriptorPath);
        string fontName = Path.GetFileNameWithoutExtension(descriptorPath);

        string resolvedTexturePath = texturePath ?? this.ResolveAtlasTexturePath(descriptorPath, fntContent);

        Texture2D? texture = _textureLoader.LoadFromFile(resolvedTexturePath, filter);
        try
        {
            BitmapFont font = BitmapFont.CreateFromBmFont(fontName, texture, fntContent);
            texture = null;
            return font;
        }
        finally
        {
            texture?.Dispose();
        }
    }

    private string ResolveAtlasTexturePath(string descriptorPath, string fntContent)
    {
        // 1. Check if descriptor specifies page file: page id=0 file="arial.png"
        ReadOnlySpan<char> span = fntContent.AsSpan();
        int pageIndex = span.IndexOf("page id=0 file=\"", StringComparison.Ordinal);
        if (pageIndex >= 0)
        {
            int start = pageIndex + "page id=0 file=\"".Length;
            int quoteEnd = span[start..].IndexOf('"');
            if (quoteEnd > 0)
            {
                string relativeName = span.Slice(start, quoteEnd).ToString();
                string? dir = Path.GetDirectoryName(descriptorPath);
                return string.IsNullOrEmpty(dir) ? relativeName : Path.Combine(dir, relativeName);
            }
        }

        // 2. Fallback: same folder, same name with .png extension
        string? descriptorDir = Path.GetDirectoryName(descriptorPath);
        string baseName = Path.GetFileNameWithoutExtension(descriptorPath);
        string candidate = descriptorDir != null ? Path.Combine(descriptorDir, baseName + ".png") : baseName + ".png";

        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException($"Could not automatically locate font atlas texture image for: {descriptorPath}");
    }
}
