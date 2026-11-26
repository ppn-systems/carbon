// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Shapes;
using Carbon.Primitives.Buffers;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.World;

namespace Carbon.Prediction.Reconciliation;

/// <summary>
/// Client-side prediction history buffer and authoritative server reconciler.
/// Rewinds and replays unacknowledged local ticks through the shared deterministic <see cref="IWorldPhysics2D"/>
/// whenever server authority diverges beyond threshold.
/// </summary>
public sealed class PredictionReconciler
{
    private readonly RingBuffer<PredictedTickRecord> _history;

    /// <summary>
    /// Gets the number of unacknowledged predicted ticks currently buffered.
    /// </summary>
    public int BufferedTickCount => _history.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="PredictionReconciler"/> class.
    /// </summary>
    /// <param name="capacity">Ring buffer capacity in ticks (rounded up to power of two, default 128).</param>
    public PredictionReconciler(int capacity = 128)
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

            _ = MovementSystem.StepSingle(
                ref pos,
                ref vel,
                drag,
                in col,
                hasCol,
                dt,
                physics);

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
            AABB2D bounds = col.GetWorldAabb(pos);
            _ = simulation.SpatialGrid.Update(localEntity.Id, in bounds);
        }

        return new ReconciliationResult(true, pos, oldPredictedPos - pos);
    }

    /// <summary>
    /// Clears all buffered predicted tick records.
    /// </summary>
    public void Clear() => _history.Clear();
}
