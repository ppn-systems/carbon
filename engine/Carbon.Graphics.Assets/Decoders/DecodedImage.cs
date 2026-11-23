// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.Assets.Decoders;

/// <summary>
/// Represents uncompressed CPU-side raw 32-bit RGBA pixel memory ready for GPU VRAM upload.
/// </summary>
public readonly struct DecodedImage : IDisposable, IEquatable<DecodedImage>
{
    private readonly byte[]? _pixels;

    /// <summary>
    /// Gets the horizontal image width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the vertical image height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets a value indicating whether valid pixel data is present.
    /// </summary>
    public bool IsValid => _pixels != null && this.Width > 0 && this.Height > 0;

    /// <summary>
    /// Gets a read-only memory span covering the raw 32-bit RGBA pixel bytes.
    /// </summary>
    public ReadOnlySpan<byte> Pixels => _pixels != null ? _pixels.AsSpan() : ReadOnlySpan<byte>.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecodedImage"/> struct.
    /// </summary>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="pixels">Raw 32-bit RGBA pixel buffer.</param>
    public DecodedImage(int width, int height, byte[] pixels)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
        }

        this.Width = width;
        this.Height = height;
        _pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // Byte array managed by GC or array pool
    }

    /// <inheritdoc/>
    public bool Equals(DecodedImage other) =>
        this.Width == other.Width && this.Height == other.Height && ReferenceEquals(_pixels, other._pixels);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is DecodedImage other && this.Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(this.Width, this.Height, _pixels);

    /// <summary>
    /// Compares two <see cref="DecodedImage"/> instances for equality.
    /// </summary>
    public static bool operator ==(DecodedImage left, DecodedImage right) =>
        left.Equals(right);

    /// <summary>
    /// Compares two <see cref="DecodedImage"/> instances for inequality.
    /// </summary>
    public static bool operator !=(DecodedImage left, DecodedImage right) =>
        !left.Equals(right);
}
