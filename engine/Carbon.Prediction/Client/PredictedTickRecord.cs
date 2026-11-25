// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Prediction.Client;

/// <summary>
/// Records a single locally predicted simulation tick and its input for potential server reconciliation replay.
/// </summary>
/// <param name="Tick">Client simulation tick index.</param>
/// <param name="MoveDirection">Directional input applied during this tick.</param>
/// <param name="BaseSpeed">Base speed in world units per second.</param>
/// <param name="PredictedPosition">Resulting locally predicted world position.</param>
/// <param name="PredictedVelocity">Resulting locally predicted linear velocity.</param>
public readonly record struct PredictedTickRecord(
    uint Tick,
    Vector2 MoveDirection,
    float BaseSpeed,
    Vector2 PredictedPosition,
    Vector2 PredictedVelocity);
