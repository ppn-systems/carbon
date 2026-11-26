// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Survival.Events;

/// <summary>
/// Emitted when an entity crosses a need threshold (e.g. Fed → Hungry), for HUD icons and notifications.
/// </summary>
/// <param name="Entity">Entity whose need changed.</param>
/// <param name="Need">Which need changed.</param>
/// <param name="Previous">Previous state, as the enum selected by <paramref name="Need"/>.</param>
/// <param name="Current">New state, as the enum selected by <paramref name="Need"/>.</param>
/// <param name="Level">Need level after the change: food/rest as a 0..1 fraction, mood as 0..100.</param>
public readonly record struct NeedStateChangedEvent(
    Entity Entity,
    NeedKind Need,
    byte Previous,
    byte Current,
    float Level);
