// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Primitives.Buffers;
using Carbon.Primitives.Math;

namespace Carbon.Prediction.Remote;

/// <summary>
/// Time-ordered interpolation buffer for remote entities, smoothing out packet jitter and permitting bounded
/// velocity extrapolation during brief packet loss.
/// </summary>
public sealed class SnapshotInterpolationBuffer
{
    private readonly RingBuffer<TransformSnapshot> _snapshots;

    /// <summary>
    /// Gets the number of snapshots currently buffered.
    /// </summary>
    public int Count => _snapshots.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotInterpolationBuffer"/> class.
    /// </summary>
    /// <param name="capacity">Maximum buffered snapshot capacity (rounded up to power of two, default 16).</param>
    public SnapshotInterpolationBuffer(int capacity = 16)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _snapshots = new RingBuffer<TransformSnapshot>(capacity);
    }

    /// <summary>
    /// Appends a snapshot in strictly increasing timestamp order, ignoring stale out-of-order packets.
    /// </summary>
    /// <param name="snapshot">Incoming remote transform snapshot.</param>
    /// <returns><see langword="true"/> if accepted; <see langword="false"/> if stale.</returns>
    public bool PushSnapshot(in TransformSnapshot snapshot)
    {
        if (!_snapshots.IsEmpty && snapshot.Timestamp <= _snapshots[^1].Timestamp)
        {
            return false;
        }

        _snapshots.PushBack(in snapshot);
        return true;
    }

    /// <summary>
    /// Samples an interpolated (or bounded-extrapolated) transform at <paramref name="renderTime"/>.
    /// </summary>
    /// <param name="renderTime">Target delayed render timestamp in seconds.</param>
    /// <param name="maxExtrapolationSeconds">Maximum dead-reckoning extrapolation window past the latest snapshot.</param>
    /// <returns>Interpolated <see cref="TransformSnapshot"/>.</returns>
    public TransformSnapshot Sample(double renderTime, float maxExtrapolationSeconds = 0.1f)
    {
        if (_snapshots.IsEmpty)
        {
            return default;
        }

        if (_snapshots.Count == 1 || renderTime <= _snapshots[0].Timestamp)
        {
            return _snapshots[0];
        }

        for (int i = 0; i < _snapshots.Count - 1; i++)
        {
            TransformSnapshot a = _snapshots[i];
            TransformSnapshot b = _snapshots[i + 1];
            if (renderTime >= a.Timestamp && renderTime <= b.Timestamp)
            {
                double span = b.Timestamp - a.Timestamp;
                float t = span > 1e-6 ? Math.Clamp((float)((renderTime - a.Timestamp) / span), 0f, 1f) : 1f;
                return new TransformSnapshot(
                    renderTime,
                    Vector2.Lerp(a.Position, b.Position, t),
                    Vector2.Lerp(a.Velocity, b.Velocity, t),
                    DeterministicMath.WrapAngle(a.Rotation + (DeterministicMath.WrapAngle(b.Rotation - a.Rotation) * t)));
            }
        }

        TransformSnapshot latest = _snapshots[^1];
        float dt = Math.Clamp((float)(renderTime - latest.Timestamp), 0f, MathF.Max(0f, maxExtrapolationSeconds));
        return new TransformSnapshot(
            renderTime,
            latest.Position + (latest.Velocity * dt),
            latest.Velocity,
            latest.Rotation);
    }

    /// <summary>
    /// Clears all buffered snapshots.
    /// </summary>
    public void Clear() => _snapshots.Clear();
}
