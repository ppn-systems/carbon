// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Prediction.Reconciliation;

/// <summary>
/// Exponentially decays visual prediction-correction offsets over render frames to eliminate abrupt snapping.
/// </summary>
public static class VisualErrorSmoother
{
    private const float SnapEpsilonSq = 0.0001f;

    /// <summary>
    /// Decays <paramref name="currentOffset"/> toward <see cref="Vector2.Zero"/> over <paramref name="deltaTime"/>.
    /// </summary>
    /// <param name="currentOffset">Current residual visual error offset.</param>
    /// <param name="deltaTime">Elapsed frame time in seconds.</param>
    /// <param name="smoothSpeed">Convergence rate per second.</param>
    /// <returns>The decayed visual offset.</returns>
    public static Vector2 Decay(Vector2 currentOffset, float deltaTime, float smoothSpeed = 12f)
    {
        if (currentOffset.LengthSquared() <= SnapEpsilonSq)
        {
            return Vector2.Zero;
        }

        if (deltaTime <= 0f || smoothSpeed <= 0f)
        {
            return currentOffset;
        }

        float factor = MathF.Max(0f, 1f - (smoothSpeed * deltaTime));
        Vector2 next = currentOffset * factor;
        return next.LengthSquared() <= SnapEpsilonSq ? Vector2.Zero : next;
    }
}
