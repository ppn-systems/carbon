// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Combat.Events;

/// <summary>
/// Emitted when an entity's <see cref="Simulation.ECS.Components.HealthComponent"/> drops to 0.
/// </summary>
/// <param name="Killer">Entity credited with the killing blow.</param>
/// <param name="Victim">Defeated entity.</param>
public readonly record struct EntityKilledEvent(Entity Killer, Entity Victim);
