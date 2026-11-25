// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Primitives.Math;

/// <summary>
/// Trigonometry built only from IEEE-754 basic operations (+, -, *, /, abs, min, max, floor), so results are
/// bit-identical on x64, ARM64 and WebAssembly.
/// </summary>
public static class DeterministicMath
{
    // Cody-Waite split of 2*pi: Hi has only 8 significant bits, so k * Hi is exact for |k| < 2^16.
    private const float TwoPiHi = 6.28125f;
    private const float TwoPiLo = 0.0019353071795864769f;
    private const float InvTwoPi = 0.15915494309189533577f;
    private const float Pi = 3.14159265358979323846f;
    private const float HalfPi = 1.57079632679489661923f;

    /// <summary>Deterministic sine (max abs error ≈ 1e-5 for |x| below 4e5).</summary>
    /// <param name="x">Angle in radians.</param>
    /// <returns>sin(x).</returns>
    public static float Sin(float x)
    {
        float r = WrapAngle(x);

        // Fold to [-pi/2, pi/2] using sin(pi - r) = sin(r)
        if (r > HalfPi)
        {
            r = Pi - r;
        }
        else if (r < -HalfPi)
        {
            r = -Pi - r;
        }

        // Odd Taylor/minimax polynomial up to r^11
        float r2 = r * r;
        float p = -2.5052108e-8f;
        p = (p * r2) + 2.7557319e-6f;
        p = (p * r2) - 1.9841270e-4f;
        p = (p * r2) + 8.3333333e-3f;
        p = (p * r2) - 1.6666667e-1f;
        return r + (r * r2 * p);
    }

    /// <summary>Deterministic cosine.</summary>
    /// <param name="x">Angle in radians.</param>
    /// <returns>cos(x).</returns>
    public static float Cos(float x) => Sin(x + HalfPi);

    /// <summary>Reduces an angle in radians into [-pi, pi] deterministically.</summary>
    /// <param name="radians">Input angle in radians.</param>
    /// <returns>Wrapped angle in [-pi, pi].</returns>
    public static float WrapAngle(float radians)
    {
        float k = Floor((radians * InvTwoPi) + 0.5f);
        return radians - (k * TwoPiHi) - (k * TwoPiLo);
    }

    /// <summary>Deterministic two-argument arctangent in [-pi, pi] (max abs error &lt; 5e-5).</summary>
    /// <param name="y">Y coordinate.</param>
    /// <param name="x">X coordinate.</param>
    /// <returns>Angle in radians.</returns>
    public static float Atan2(float y, float x)
    {
        if (x == 0f && y == 0f)
        {
            return 0f;
        }

        float ax = MathF.Abs(x);
        float ay = MathF.Abs(y);
        float a = MathF.Min(ax, ay) / MathF.Max(ax, ay);
        float s = a * a;

        // Minimax polynomial for atan(a) on [0, 1]
        float poly = 0.9998660f + (s * (-0.3302995f + (s * (0.1801410f + (s * (-0.0851330f + (s * 0.0208351f)))))));
        float r = a * poly;

        if (ay > ax)
        {
            r = HalfPi - r;
        }

        if (x < 0f)
        {
            r = Pi - r;
        }

        if (y < 0f)
        {
            r = -r;
        }

        return r;
    }

    private static float Floor(float x)
    {
        int i = (int)x;
        return (x < i) ? i - 1 : i;
    }
}
