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

        float normalized = (total / maxAmplitude * 0.62f) + 0.5f;
        return Math.Clamp(normalized, 0f, 1f);
    }

    /// <summary>
    /// Evaluates full-detail fBm and captures a smoother coarse octave pass simultaneously in a single loop.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="fullOctaves">Total octave count for the full value.</param>
    /// <param name="coarseOctaves">Prefix octave count for the coarse value.</param>
    /// <param name="coarseValue">Outputs the normalized coarse fBm value in <c>[0, 1]</c>.</param>
    /// <param name="lacunarity">Frequency multiplier per octave.</param>
    /// <param name="persistence">Amplitude persistence per octave.</param>
    /// <returns>Normalized full-octave noise value in <c>[0, 1]</c>.</returns>
    public float EvaluateWithCoarse(
        float x,
        float y,
        int fullOctaves,
        int coarseOctaves,
        out float coarseValue,
        float lacunarity = 2.0f,
        float persistence = 0.5f)
    {
        float total = 0f;
        float frequency = 1f;
        float amplitude = 1f;
        float maxAmplitude = 0f;
        float coarseTotal = 0f;
        float coarseMax = 0f;

        for (int i = 0; i < fullOctaves; i++)
        {
            total += _noise.Evaluate(x * frequency, y * frequency) * amplitude;
            maxAmplitude += amplitude;

            if (i < coarseOctaves)
            {
                coarseTotal = total;
                coarseMax = maxAmplitude;
            }

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        coarseValue = coarseMax > 0f ? Math.Clamp((coarseTotal / coarseMax * 0.62f) + 0.5f, 0f, 1f) : 0.5f;
        return Math.Clamp((total / maxAmplitude * 0.62f) + 0.5f, 0f, 1f);
    }

    /// <summary>
    /// Evaluates true Musgrave per-octave ridged multifractal noise with crest weighting, returning
    /// both full and coarse normalized values in <c>[0, 1]</c>.
    /// </summary>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <param name="fullOctaves">Full octave count.</param>
    /// <param name="coarseOctaves">Coarse octave count.</param>
    /// <param name="coarseRidge">Outputs the coarse ridged value in <c>[0, 1]</c>.</param>
    /// <param name="lacunarity">Frequency multiplier per octave.</param>
    /// <param name="gain">Weight multiplier carried from lower octaves to crests.</param>
    /// <returns>Normalized full ridged value in <c>[0, 1]</c>.</returns>
    public float EvaluateRidged(
        float x,
        float y,
        int fullOctaves,
        int coarseOctaves,
        out float coarseRidge,
        float lacunarity = 2.0f,
        float gain = 1.8f)
    {
        float total = 0f;
        float maxWeight = 0f;
        float frequency = 1f;
        float amplitude = 1f;
        float weight = 1f;
        float coarseTotal = 0f;
        float coarseMax = 0f;

        for (int i = 0; i < fullOctaves; i++)
        {
            float n = 1f - MathF.Abs(_noise.Evaluate(x * frequency, y * frequency));
            float signal = n * n * weight;
            weight = Math.Clamp(signal * gain, 0f, 1f);
            total += signal * amplitude;
            maxWeight += amplitude;

            if (i < coarseOctaves)
            {
                coarseTotal = total;
                coarseMax = maxWeight;
            }

            frequency *= lacunarity;
            amplitude *= 0.5f;
        }

        coarseRidge = coarseMax > 0f ? Math.Clamp(coarseTotal / coarseMax, 0f, 1f) : 0f;
        return maxWeight > 0f ? Math.Clamp(total / maxWeight, 0f, 1f) : 0f;
    }
}
