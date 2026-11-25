// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Items.Components;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.World;

namespace Carbon.Items.Systems;

/// <summary>
/// Simulation system running during <see cref="SimulationPhase.PostUpdate"/> that despawns ground
/// <see cref="LootDropComponent"/> entities whose expiration tick has elapsed.
/// </summary>
public sealed class LootExpirationSystem : ISimulationSystem
{
    private readonly List<Entity> _expired = new(32);

    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.PostUpdate;

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        _expired.Clear();
        EntityManager entities = world.Entities;
        ComponentPool<LootDropComponent> pool = entities.GetPool<LootDropComponent>();
        ReadOnlySpan<int> slots = pool.EntitySlots;
        ReadOnlySpan<LootDropComponent> drops = pool.AsSpan();

        for (int i = 0; i < slots.Length; i++)
        {
            ulong despawnAt = drops[i].DespawnAtTick;
            if (despawnAt != 0 && tick.TickIndex >= despawnAt)
            {
                Entity entity = entities.GetEntity(slots[i]);
                if (entity.IsValid)
                {
                    _expired.Add(entity);
                }
            }
        }

        for (int i = 0; i < _expired.Count; i++)
        {
            world.DespawnEntity(_expired[i]);
        }
    }
}
