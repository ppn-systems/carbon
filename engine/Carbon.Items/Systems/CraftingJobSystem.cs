// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Components;
using Carbon.Items.Events;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.World;

namespace Carbon.Items.Systems;

/// <summary>
/// Fixed-tick system advancing every running <see cref="CraftingJobComponent"/> by its skill-scaled work rate and
/// publishing <see cref="CraftingJobCompletedEvent"/> when one finishes. Zero allocations per tick.
/// </summary>
public sealed class CraftingJobSystem : ISimulationSystem
{
    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.PostUpdate;

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        EntityManager entities = world.Entities;
        ComponentPool<CraftingJobComponent> pool = entities.GetPool<CraftingJobComponent>();
        ReadOnlySpan<int> slots = pool.EntitySlots;
        Span<CraftingJobComponent> jobs = pool.AsSpan();

        for (int i = 0; i < jobs.Length; i++)
        {
            CraftingJobComponent job = jobs[i];
            if (job.State != CraftingJobState.Running)
            {
                continue;
            }

            CraftingJobComponent advanced = job.Advance();
            jobs[i] = advanced;
            if (advanced.State == CraftingJobState.Completed)
            {
                world.PublishEvent(new CraftingJobCompletedEvent(entities.GetEntity(slots[i]), advanced.RecipeId));
            }
        }
    }
}
