// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Primitives.Random;

namespace Carbon.Maps.Noise;

/// <summary>
/// Deterministic, zero-allocation 2D Simplex noise generator.
/// Evaluates smooth, continuous coherent gradient noise in the range [-1.0, 1.0].
/// </summary>
public sealed class SimplexNoise2D
{
    private const float F2 = 0.366025403784439f; // 0.5 * (sqrt(3) - 1)
    private const float G2 = 0.211324865405187f; // (3 - sqrt(3)) / 6

    private static readonly Vector2[] s_gradients =
    [
        new(1f, 1f),
        new(-1f, 1f),
        new(1f, -1f),
        new(-1f, -1f),
        new(1f, 0f),
        new(-1f, 0f),
        new(0f, 1f),
        new(0f, -1f)
    ];

    private readonly byte[] _perm = new byte[512];

    /// <summary>
    /// Initializes a new instance of the <see cref="SimplexNoise2D"/> class with a deterministic seed.
    /// </summary>
    /// <param name="seed">Seed for permutation table generation.</param>
    public SimplexNoise2D(ulong seed)
    {
        byte[] source = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            source[i] = (byte)i;
        }

        DeterministicRandom rng = new(seed);
        for (int i = 255; i > 0; i--)
        {
            int swapIdx = rng.NextInt(0, i + 1);
            (source[i], source[swapIdx]) = (source[swapIdx], source[i]);
        }

        for (int i = 0; i < 512; i++)
        {
            _perm[i] = source[i & 255];
        }
    }

    /// <summary>
    /// Evaluates 2D simplex noise at the specified continuous coordinates.
    /// </summary>
    /// <param name="x">X world coordinate.</param>
    /// <param name="y">Y world coordinate.</param>
    /// <returns>Noise value normalized in the range [-1.0, 1.0].</returns>
    public float Evaluate(float x, float y)
    {
        float s = (x + y) * F2;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);

        float t = (i + j) * G2;
        float x0 = x - (i - t);
        float y0 = y - (j - t);

        int i1;
        int j1;
        if (x0 > y0)
        {
            i1 = 1;
            j1 = 0;
        }
        else
        {
            i1 = 0;
            j1 = 1;
        }

        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + (2f * G2);
        float y2 = y0 - 1f + (2f * G2);

        int ii = i & 255;
        int jj = j & 255;

        float n0 = 0f;
        float n1 = 0f;
        float n2 = 0f;

        float t0 = 0.5f - (x0 * x0) - (y0 * y0);
        if (t0 > 0f)
        {
            t0 *= t0;
            int gi0 = _perm[ii + _perm[jj]] & 7;
            n0 = t0 * t0 * Vector2.Dot(s_gradients[gi0], new Vector2(x0, y0));
        }

        float t1 = 0.5f - (x1 * x1) - (y1 * y1);
        if (t1 > 0f)
        {
            t1 *= t1;
            int gi1 = _perm[ii + i1 + _perm[jj + j1]] & 7;
            n1 = t1 * t1 * Vector2.Dot(s_gradients[gi1], new Vector2(x1, y1));
        }

        float t2 = 0.5f - (x2 * x2) - (y2 * y2);
        if (t2 > 0f)
        {
            t2 *= t2;
            int gi2 = _perm[ii + 1 + _perm[jj + 1]] & 7;
            n2 = t2 * t2 * Vector2.Dot(s_gradients[gi2], new Vector2(x2, y2));
        }

        // Scale result to [-1, 1]
        return 70.0f * (n0 + n1 + n2);
    }

    private static int FastFloor(float x) =>
        x >= 0f ? (int)x : (int)x - 1;
}
