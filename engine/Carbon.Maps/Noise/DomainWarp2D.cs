// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Noise;

/// <summary>
/// Implements 2D domain warping (coordinate perturbation).
/// Distorts spatial coordinates with dual vector noise fields to create realistic, organic coastlines, river curves, and mountain ridges.
/// </summary>
public sealed class DomainWarp2D
{
    private readonly FractalNoise2D _warpX;
    private readonly FractalNoise2D _warpY;
    private readonly FractalNoise2D _source;

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainWarp2D"/> class.
    /// </summary>
    /// <param name="seed">Deterministic seed for coordinate warping.</param>
    public DomainWarp2D(ulong seed)
    {
        _warpX = new FractalNoise2D(seed ^ 0x5555555555555555UL);
        _warpY = new FractalNoise2D(seed ^ 0xAAAAAAAAAAAAAAAAUL);
        _source = new FractalNoise2D(seed);
    }

    /// <summary>
    /// Evaluates domain-warped noise at the given coordinates.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="warpStrength">Intensity of the coordinate distortion.</param>
    /// <param name="octaves">Octaves of the underlying noise.</param>
    /// <returns>Domain-warped noise value in [0.0, 1.0].</returns>
    public float Evaluate(float x, float y, float warpStrength = 0.2f, int octaves = 4)
    {
        float qx = (_warpX.Evaluate(x, y, 2) * 2f) - 1f;
        float qy = (_warpY.Evaluate(x + 5.2f, y + 1.3f, 2) * 2f) - 1f;

        float warpedX = x + (qx * warpStrength);
        float warpedY = y + (qy * warpStrength);

        return _source.Evaluate(warpedX, warpedY, octaves);
    }
}
