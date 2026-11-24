// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Random;

/// <summary>
/// Trigonometry built only from IEEE-754 basic operations (+, -, *, floor), so results are
/// bit-identical on x64, ARM64 and WebAssembly. <see cref="System.MathF.Sin(float)"/> is not
/// guaranteed to be, and world generation feeds gameplay state shared by server and clients.
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
        // Range-reduce to [-pi, pi]
        float k = Floor((x * InvTwoPi) + 0.5f);
        float r = x - (k * TwoPiHi) - (k * TwoPiLo);

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

    private static float Floor(float x)
    {
        int i = (int)x;
        return (x < i) ? i - 1 : i;
    }
}
