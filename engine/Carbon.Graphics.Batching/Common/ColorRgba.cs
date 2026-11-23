// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Graphics.Batching.Common;

/// <summary>
/// Represents a 32-bit packed RGBA color stored as 4 unmanaged bytes (R, G, B, A).
/// </summary>
/// <param name="R">Red color component (0-255).</param>
/// <param name="G">Green color component (0-255).</param>
/// <param name="B">Blue color component (0-255).</param>
/// <param name="A">Alpha transparency component (0-255).</param>
public readonly record struct ColorRgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>
    /// Gets the color packed into a 32-bit unsigned integer in little-endian order (0xAABBGGRR).
    /// </summary>
    public uint PackedValue => (uint)(this.R | (this.G << 8) | (this.B << 16) | (this.A << 24));

    /// <summary>
    /// Pure white color (255, 255, 255, 255).
    /// </summary>
    public static readonly ColorRgba White = new(255, 255, 255, 255);

    /// <summary>
    /// Pure black color (0, 0, 0, 255).
    /// </summary>
    public static readonly ColorRgba Black = new(0, 0, 0, 255);

    /// <summary>
    /// Fully transparent color (0, 0, 0, 0).
    /// </summary>
    public static readonly ColorRgba Transparent = new(0, 0, 0, 0);

    /// <summary>
    /// Pure red color (255, 0, 0, 255).
    /// </summary>
    public static readonly ColorRgba Red = new(255, 0, 0, 255);

    /// <summary>
    /// Pure green color (0, 255, 0, 255).
    /// </summary>
    public static readonly ColorRgba Green = new(0, 255, 0, 255);

    /// <summary>
    /// Pure blue color (0, 0, 255, 255).
    /// </summary>
    public static readonly ColorRgba Blue = new(0, 0, 255, 255);

    /// <summary>
    /// Pure yellow color (255, 255, 0, 255).
    /// </summary>
    public static readonly ColorRgba Yellow = new(255, 255, 0, 255);

    /// <summary>
    /// Cyan color (0, 255, 255, 255).
    /// </summary>
    public static readonly ColorRgba Cyan = new(0, 255, 255, 255);

    /// <summary>
    /// Magenta color (255, 0, 255, 255).
    /// </summary>
    public static readonly ColorRgba Magenta = new(255, 0, 255, 255);

    /// <summary>
    /// Medium gray color (128, 128, 128, 255).
    /// </summary>
    public static readonly ColorRgba Gray = new(128, 128, 128, 255);

    /// <summary>
    /// Creates a <see cref="ColorRgba"/> from normalized floating point components [0.0 - 1.0].
    /// </summary>
    /// <param name="r">Normalized red.</param>
    /// <param name="g">Normalized green.</param>
    /// <param name="b">Normalized blue.</param>
    /// <param name="a">Normalized alpha.</param>
    /// <returns>A new <see cref="ColorRgba"/> instance.</returns>
    public static ColorRgba FromNormalized(float r, float g, float b, float a = 1.0f) => new(
        (byte)(Math.Clamp(r, 0f, 1f) * 255f),
        (byte)(Math.Clamp(g, 0f, 1f) * 255f),
        (byte)(Math.Clamp(b, 0f, 1f) * 255f),
        (byte)(Math.Clamp(a, 0f, 1f) * 255f));

    /// <summary>
    /// Converts this color to a normalized floating-point <see cref="Vector4"/>.
    /// </summary>
    /// <returns>A vector containing (R, G, B, A) in [0.0, 1.0] range.</returns>
    public Vector4 ToVector4() => new(this.R / 255f, this.G / 255f, this.B / 255f, this.A / 255f);
}
