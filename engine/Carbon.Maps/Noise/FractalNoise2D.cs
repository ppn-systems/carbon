// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Maps.Noise;

/// <summary>
/// Evaluates multi-octave Fractal Brownian Motion (fBm) using 2D Simplex noise.
/// Produces natural, self-similar terrain elevation and climate patterns.
/// </summary>
public sealed class FractalNoise2D
{
    private readonly SimplexNoise2D _noise;

    /// <summary>
    /// Initializes a new instance of the <see cref="FractalNoise2D"/> class.
    /// </summary>
    /// <param name="seed">Deterministic noise seed.</param>
    public FractalNoise2D(ulong seed) =>
        _noise = new SimplexNoise2D(seed);

    /// <summary>
    /// Evaluates multi-octave fBm at the given coordinates, returning a normalized value in [0.0, 1.0].
    /// </summary>
    /// <param name="x">X world coordinate.</param>
    /// <param name="y">Y world coordinate.</param>
    /// <param name="octaves">Number of harmonic layers (e.g. 4 to 6).</param>
    /// <param name="lacunarity">Frequency multiplier per octave (typically 2.0).</param>
    /// <param name="persistence">Amplitude dampening factor per octave (typically 0.5).</param>
    /// <returns>Normalized noise value in the range [0.0, 1.0].</returns>
    public float Evaluate(float x, float y, int octaves = 4, float lacunarity = 2.0f, float persistence = 0.5f)
    {
        float total = 0f;
        float frequency = 1f;
        float amplitude = 1f;
        float maxAmplitude = 0f;

        for (int i = 0; i < octaves; i++)
        {
            total += _noise.Evaluate(x * frequency, y * frequency) * amplitude;
            maxAmplitude += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        float normalized = (total / maxAmplitude * 0.5f) + 0.5f;
        return Math.Clamp(normalized, 0f, 1f);
    }
}
