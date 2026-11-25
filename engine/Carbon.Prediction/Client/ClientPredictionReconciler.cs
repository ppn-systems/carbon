// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Primitives.Buffers;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;

namespace Carbon.Prediction.Client;

/// <summary>
/// Client-side prediction history buffer and authoritative server reconciler.
/// Rewinds and replays unacknowledged local ticks through the shared deterministic <see cref="IWorldPhysics2D"/>
/// whenever server authority diverges beyond threshold.
/// </summary>
public sealed class ClientPredictionReconciler
{
    private readonly RingBuffer<PredictedTickRecord> _history;

    /// <summary>
    /// Gets the number of unacknowledged predicted ticks currently buffered.
    /// </summary>
    public int BufferedTickCount => _history.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientPredictionReconciler"/> class.
    /// </summary>
    /// <param name="capacity">Ring buffer capacity in ticks (rounded up to power of two, default 128).</param>
    public ClientPredictionReconciler(int capacity = 128)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _history = new RingBuffer<PredictedTickRecord>(capacity);
    }

    /// <summary>
    /// Records the input and predicted outcome of a local client simulation tick.
    /// </summary>
    /// <param name="record">Predicted tick record.</param>
    public void RecordPredictedTick(in PredictedTickRecord record) => _history.PushBack(in record);

    /// <summary>
    /// Reconciles buffered local predictions against an authoritative server state snapshot for
    /// <paramref name="authoritativeTick"/>, replaying any newer unacknowledged ticks on mismatch.
    /// </summary>
    /// <param name="authoritativeTick">Tick acknowledged by the server.</param>
    /// <param name="authoritativePosition">Authoritative entity position at <paramref name="authoritativeTick"/>.</param>
    /// <param name="authoritativeVelocity">Authoritative entity linear velocity at <paramref name="authoritativeTick"/>.</param>
    /// <param name="simulation">Local world simulation hosting the predicted entity and physics provider.</param>
    /// <param name="localEntity">Local player entity handle.</param>
    /// <param name="errorThreshold">Maximum allowable position error before triggering rewind + replay.</param>
    /// <returns>The <see cref="ReconciliationResult"/>.</returns>
    public ReconciliationResult Reconcile(
        uint authoritativeTick,
        Vector2 authoritativePosition,
        Vector2 authoritativeVelocity,
        WorldSimulation simulation,
        Entity localEntity,
        float errorThreshold = 1.5f)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentOutOfRangeException.ThrowIfNegative(errorThreshold);

        if (!simulation.Entities.IsAlive(localEntity) || !simulation.Entities.HasTransform(localEntity))
        {
            return new ReconciliationResult(false, authoritativePosition, Vector2.Zero);
        }

        while (!_history.IsEmpty && _history[0].Tick < authoritativeTick)
        {
            _ = _history.TryPopFront(out _);
        }

        Vector2 currentPos = simulation.Entities.GetTransform(localEntity).Position;

        if (_history.IsEmpty || _history[0].Tick != authoritativeTick)
        {
            return new ReconciliationResult(false, currentPos, Vector2.Zero);
        }

        PredictedTickRecord acknowledged = _history[0];
        _ = _history.TryPopFront(out _);

        float errorSq = Vector2.DistanceSquared(acknowledged.PredictedPosition, authoritativePosition);
        if (errorSq <= errorThreshold * errorThreshold)
        {
            return new ReconciliationResult(false, currentPos, Vector2.Zero);
        }

        // Misprediction detected: rewind to authoritative state and replay remaining unacknowledged ticks
        Vector2 oldPredictedPos = currentPos;
        Vector2 pos = authoritativePosition;
        Vector2 vel = authoritativeVelocity;

        float dt = simulation.Clock.DeltaTime;
        float drag = simulation.Entities.HasVelocity(localEntity) ? simulation.Entities.GetVelocity(localEntity).Drag : 0f;
        IWorldPhysics2D? physics = simulation.WorldPhysics ?? simulation.ObstacleGrid;
        bool hasCol = simulation.Entities.HasCollider(localEntity);
        Collider2D col = hasCol ? simulation.Entities.GetCollider(localEntity) : default;

        for (int i = 0; i < _history.Count; i++)
        {
            ref PredictedTickRecord future = ref _history[i];

            if (future.MoveDirection.LengthSquared() > 0.000001f && future.BaseSpeed > 0f)
            {
                Vector2 dir = Vector2.Normalize(future.MoveDirection);
                float multiplier = physics is not null ? physics.GetMoveSpeedMultiplier(pos) : 1f;
                vel = dir * (future.BaseSpeed * multiplier);
            }
            else if (drag <= 0f)
            {
                vel = Vector2.Zero;
            }

            Vector2 displacement = vel * dt;
            if (drag > 0f)
            {
                vel *= MathF.Max(0f, 1f - (drag * dt));
            }

            if (physics is not null && hasCol && !col.IsTrigger && col.Kind != ColliderKind.None)
            {
                float radius = col.Kind == ColliderKind.Circle
                    ? col.Radius
                    : MathF.Min(col.Box.Width, col.Box.Height) * 0.5f;
                Vector2 centerOffset = col.Kind == ColliderKind.Aabb ? col.Box.Center : Vector2.Zero;

                if (radius > 0f)
                {
                    float totalDist = displacement.Length();
                    if (totalDist > 0.0001f)
                    {
                        float maxStep = MathF.Min(2.5f, radius * 0.5f);
                        int subSteps = Math.Clamp((int)MathF.Ceiling(totalDist / maxStep), 1, 16);
                        Vector2 stepDelta = displacement / subSteps;
                        Vector2 currentCenter = pos + centerOffset;

                        for (int step = 0; step < subSteps; step++)
                        {
                            currentCenter += stepDelta;
                            if (physics.ResolveCircleCollision(ref currentCenter, radius, out Vector2 pushNormal))
                            {
                                float stepDot = Vector2.Dot(stepDelta, pushNormal);
                                if (stepDot < 0f)
                                {
                                    stepDelta -= pushNormal * stepDot;
                                }

                                float velDot = Vector2.Dot(vel, pushNormal);
                                if (velDot < 0f)
                                {
                                    vel -= pushNormal * velDot;
                                }
                            }
                        }

                        pos = currentCenter - centerOffset;
                    }
                    else
                    {
                        Vector2 currentCenter = pos + displacement + centerOffset;
                        _ = physics.ResolveCircleCollision(ref currentCenter, radius, out _);
                        pos = currentCenter - centerOffset;
                    }
                }
                else
                {
                    pos += displacement;
                }
            }
            else
            {
                pos += displacement;
            }

            future = future with
            {
                PredictedPosition = pos,
                PredictedVelocity = vel
            };
        }

        ref Transform2D transform = ref simulation.Entities.GetTransform(localEntity);
        transform.Position = pos;
        simulation.Entities.SetPreviousTransform(localEntity, new PreviousTransform2D(pos, transform.Rotation));
        if (simulation.Entities.HasVelocity(localEntity))
        {
            simulation.Entities.GetVelocity(localEntity).Linear = vel;
        }

        if (hasCol)
        {
            _ = simulation.SpatialGrid.Update(localEntity.Id, pos);
        }

        return new ReconciliationResult(true, pos, oldPredictedPos - pos);
    }

    /// <summary>
    /// Clears all buffered predicted tick records.
    /// </summary>
    public void Clear() => _history.Clear();
}
