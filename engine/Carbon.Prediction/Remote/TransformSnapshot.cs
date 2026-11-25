// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Prediction.Remote;

/// <summary>
/// Timestamped network transform snapshot received for a remote entity.
/// </summary>
/// <param name="Timestamp">Remote simulation or server receive time in seconds.</param>
/// <param name="Position">World position at <paramref name="Timestamp"/>.</param>
/// <param name="Velocity">Linear velocity at <paramref name="Timestamp"/>.</param>
/// <param name="Rotation">Facing rotation in radians.</param>
public readonly record struct TransformSnapshot(
    double Timestamp,
    Vector2 Position,
    Vector2 Velocity,
    float Rotation = 0f);
