// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using Carbon.Graphics.Animation.Atlas;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.OpenGL.Textures;

namespace Carbon.Graphics.Assets.Loaders;

/// <summary>
/// Loads sprite sheet atlas descriptors and coordinates companion texture uploads into <see cref="SpriteAtlas"/>.
/// </summary>
public sealed class AtlasLoader
{
    private readonly TextureLoader _textureLoader;

    /// <summary>
    /// Initializes a new instance of the <see cref="AtlasLoader"/> class.
    /// </summary>
    /// <param name="textureLoader">The underlying texture loader.</param>
    public AtlasLoader(TextureLoader textureLoader) =>
        _textureLoader = textureLoader ?? throw new ArgumentNullException(nameof(textureLoader));

    /// <summary>
    /// Creates a uniform grid sprite atlas from an image file divided into regular cell dimensions.
    /// </summary>
    /// <param name="texturePath">Path to the sprite sheet image.</param>
    /// <param name="cellWidth">Width of each sprite cell in pixels.</param>
    /// <param name="cellHeight">Height of each sprite cell in pixels.</param>
    /// <param name="prefix">Frame region name prefix.</param>
    /// <param name="filter">Texture sampling filter.</param>
    /// <returns>A populated <see cref="SpriteAtlas"/> instance.</returns>
    public SpriteAtlas LoadGridAtlas(
        string texturePath,
        int cellWidth,
        int cellHeight,
        string prefix = "frame",
        TextureFilter filter = TextureFilter.Nearest)
    {
        Texture2D? texture = _textureLoader.LoadFromFile(texturePath, filter);
        try
        {
            SpriteAtlas atlas = SpriteAtlas.CreateGrid(texture, cellWidth, cellHeight, prefix: prefix);
            texture = null;
            return atlas;
        }
        finally
        {
            texture?.Dispose();
        }
    }

    /// <summary>
    /// Loads a sprite atlas from a simple space-delimited or CSV region descriptor file (name x y width height [originX originY]).
    /// </summary>
    /// <param name="descriptorPath">Path to the text descriptor file.</param>
    /// <param name="texturePath">Optional explicit path to the atlas image (inferred if null).</param>
    /// <param name="filter">Texture sampling filter.</param>
    /// <returns>A populated <see cref="SpriteAtlas"/> instance.</returns>
    public SpriteAtlas LoadFromFile(
        string descriptorPath,
        string? texturePath = null,
        TextureFilter filter = TextureFilter.Nearest)
    {
        ArgumentException.ThrowIfNullOrEmpty(descriptorPath, nameof(descriptorPath));

        if (!File.Exists(descriptorPath))
        {
            throw new FileNotFoundException($"Atlas descriptor file not found: {descriptorPath}", descriptorPath);
        }

        string resolvedTexturePath = texturePath ?? ResolveAtlasTexturePath(descriptorPath);
        Texture2D? texture = _textureLoader.LoadFromFile(resolvedTexturePath, filter);
        try
        {
            SpriteAtlas atlas = new(texture);

            string[] lines = File.ReadAllLines(descriptorPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] tokens = line.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length >= 5)
                {
                    string name = tokens[0];
                    if (float.TryParse(tokens[1], out float x)
                        && float.TryParse(tokens[2], out float y)
                        && float.TryParse(tokens[3], out float w)
                        && float.TryParse(tokens[4], out float h))
                    {
                        Vector2? origin = null;
                        if (tokens.Length >= 7 && float.TryParse(tokens[5], out float ox) && float.TryParse(tokens[6], out float oy))
                        {
                            origin = new Vector2(ox, oy);
                        }

                        atlas.AddRegion(new AtlasRegion(texture, new RectangleF(x, y, w, h), name, origin));
                    }
                }
            }

            texture = null;
            return atlas;
        }
        finally
        {
            texture?.Dispose();
        }
    }

    private static string ResolveAtlasTexturePath(string descriptorPath)
    {
        string? dir = Path.GetDirectoryName(descriptorPath);
        string baseName = Path.GetFileNameWithoutExtension(descriptorPath);
        string candidate = dir != null ? Path.Combine(dir, baseName + ".png") : baseName + ".png";

        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException($"Could not automatically locate sprite atlas image for: {descriptorPath}");
    }
}
