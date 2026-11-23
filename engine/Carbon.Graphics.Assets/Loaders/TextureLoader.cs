// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using Carbon.Graphics.Assets.Decoders;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.Textures;

namespace Carbon.Graphics.Assets.Loaders;

/// <summary>
/// Dedicated loader for decoding image files from disk or streams and uploading raw pixels to GPU <see cref="Texture2D"/> VRAM.
/// </summary>
public sealed class TextureLoader
{
    private readonly GraphicsDevice _device;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextureLoader"/> class.
    /// </summary>
    /// <param name="device">The concrete graphics device providing GL API and state cache.</param>
    public TextureLoader(GraphicsDevice device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    /// <summary>
    /// Loads an image file from disk and uploads its 32-bit RGBA pixel data to GPU VRAM.
    /// </summary>
    /// <param name="filePath">The file path to the image.</param>
    /// <param name="filter">The texture sampling filter.</param>
    /// <returns>A populated <see cref="Texture2D"/> instance.</returns>
    public Texture2D LoadFromFile(string filePath, TextureFilter filter = TextureFilter.Nearest)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath, nameof(filePath));

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Image file not found: {filePath}", filePath);
        }

        byte[] fileBytes = File.ReadAllBytes(filePath);
        return this.LoadFromMemory(fileBytes.AsSpan(), filter);
    }

    /// <summary>
    /// Decodes an image from memory bytes and uploads its 32-bit RGBA pixel data to GPU VRAM.
    /// </summary>
    /// <param name="data">The raw file bytes (PNG, QOI, BMP, JPEG).</param>
    /// <param name="filter">The texture sampling filter.</param>
    /// <returns>A populated <see cref="Texture2D"/> instance.</returns>
    public Texture2D LoadFromMemory(ReadOnlySpan<byte> data, TextureFilter filter = TextureFilter.Nearest)
    {
        DecodedImage decoded = ImageDecoder.Decode(data);

        Texture2D texture = new(
            _device.Gl,
            _device.StateCache,
            (uint)decoded.Width,
            (uint)decoded.Height,
            TextureFormat.Rgba8,
            filter,
            TextureWrap.ClampToEdge);

        texture.SetData(decoded.Pixels);
        return texture;
    }

    /// <summary>
    /// Decodes an image from a readable stream and uploads to GPU VRAM.
    /// </summary>
    /// <param name="stream">The source readable stream.</param>
    /// <param name="filter">The texture sampling filter.</param>
    /// <returns>A populated <see cref="Texture2D"/> instance.</returns>
    public Texture2D LoadFromStream(Stream stream, TextureFilter filter = TextureFilter.Nearest)
    {
        ArgumentNullException.ThrowIfNull(stream, nameof(stream));

        DecodedImage decoded = ImageDecoder.Decode(stream);

        Texture2D texture = new(
            _device.Gl,
            _device.StateCache,
            (uint)decoded.Width,
            (uint)decoded.Height,
            TextureFormat.Rgba8,
            filter,
            TextureWrap.ClampToEdge);

        texture.SetData(decoded.Pixels);
        return texture;
    }
}
