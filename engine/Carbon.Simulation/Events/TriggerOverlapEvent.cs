// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Simulation.Events;

/// <summary>
/// Emitted during a simulation tick when a trigger collider overlaps another entity collider.
/// </summary>
/// <param name="TriggerEntity">The entity owning the trigger collider.</param>
/// <param name="OtherEntity">The overlapping entity.</param>
public readonly record struct TriggerOverlapEvent(Entity TriggerEntity, Entity OtherEntity);
