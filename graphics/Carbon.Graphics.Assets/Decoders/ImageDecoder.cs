// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using StbImageSharp;

namespace Carbon.Graphics.Assets.Decoders;

/// <summary>
/// Universal image decoder supporting PNG, JPEG, BMP, TGA, and QOI formats.
/// Zero native DLL dependencies; 100% Native AOT and trimming compliant.
/// </summary>
public static class ImageDecoder
{
    /// <summary>
    /// Decodes an image from raw memory bytes into an uncompressed 32-bit RGBA pixel buffer.
    /// </summary>
    /// <param name="data">The encoded file byte span.</param>
    /// <returns>A populated <see cref="DecodedImage"/> containing 32-bit RGBA pixel memory.</returns>
    public static DecodedImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException("Image byte data cannot be empty.", nameof(data));
        }

        // 1. Fast-path QOI check
        if (QoiDecoder.IsQoi(data))
        {
            return QoiDecoder.Decode(data);
        }

        // 2. Decode standard PNG / JPEG / BMP / TGA via StbImageSharp
        byte[] array = data.ToArray();
        ImageResult result = ImageResult.FromMemory(array, ColorComponents.RedGreenBlueAlpha);

        return new DecodedImage(result.Width, result.Height, result.Data);
    }

    /// <summary>
    /// Reads and decodes an image from a stream.
    /// </summary>
    /// <param name="stream">The source readable stream.</param>
    /// <returns>A populated <see cref="DecodedImage"/>.</returns>
    public static DecodedImage Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream, nameof(stream));

        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return Decode(ms.ToArray().AsSpan());
    }
}
