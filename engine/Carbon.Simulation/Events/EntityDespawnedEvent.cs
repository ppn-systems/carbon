// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Simulation.Events;

/// <summary>
/// Emitted when an entity is despawned from <see cref="World.WorldSimulation"/>.
/// </summary>
/// <param name="Entity">The local entity handle that was despawned.</param>
/// <param name="NetworkId">The entity's associated network identifier, or 0 if local-only.</param>
public readonly record struct EntityDespawnedEvent(Entity Entity, ulong NetworkId);
