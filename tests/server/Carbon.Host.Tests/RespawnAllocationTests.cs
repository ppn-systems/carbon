// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Creatures.Components;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Carbon.Testing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Host.Tests;

/// <summary>
/// ADR-044 (audit round 3, ALLOC-11): every kill despawns a monster, and its cell respawns one after
/// <c>respawnDelayTicks</c>. The tick guards never see this, because nothing dies in their windows, so this guard kills
/// every live monster through the real world (as <c>CombatService</c> does: dead, then despawned) and measures a whole
/// kill-and-respawn cycle.
/// </summary>
public sealed class RespawnAllocationTests(ITestOutputHelper output)
{
    /// <summary>Respawn delay (1800) plus scan slack, so every emptied cell has respawned by the end of a cycle.</summary>
    private const int CycleTicks = 2400;

    [Fact]
    public void KillAndRespawnCycle_SteadyState_AllocatesPerRespawn()
    {
        using TickAllocationHarness harness = TickAllocationHarness.Create(players: 8);
        Cycle cycle = new(harness);
        for (int i = 0; i < 900; i++)
        {
            harness.Step();
        }

        // Warm-up cycles grow the entity, component and attachment capacities to the peak monster count.
        AllocationResult window = AllocationProbe.Measure(1, warmupIterations: 2, i => cycle.Run());
        int respawns = cycle.Respawns[2];
        int repeatRespawns = cycle.Respawns[3];
        output.WriteLine($"{(double)window.Bytes / respawns:F1} B per respawn over {respawns} respawns, " +
            $"{(double)window.RepeatBytes!.Value / repeatRespawns:F1} over {repeatRespawns} in the repeat window ({window})");

        respawns.Should().BeGreaterThan(20, "the measured cycle respawns the monsters it killed");
        window.Bytes.Should().BeGreaterThan(0, $"characterisation: a respawn allocates its StatSheet and ThreatTable again ({window})");
    }

    /// <summary>Kills every live monster, then ticks until their cells have respawned.</summary>
    private sealed class Cycle(TickAllocationHarness harness)
    {
        private readonly Entity[] _victims = new Entity[1024];

        private int _runs;

        /// <summary>Gets the monsters respawned by each run, in order: two warm-up runs, the window, the repeat window.</summary>
        public int[] Respawns { get; } = new int[4];

        public void Run()
        {
            WorldSimulation simulation = harness.World.Simulation;
            ComponentPool<MonsterComponent> monsters = simulation.Entities.GetPool<MonsterComponent>();
            int count = 0;
            for (int i = 0; i < monsters.EntitySlots.Length; i++)
            {
                _victims[count++] = simulation.Entities.GetEntity(monsters.EntitySlots[i]);
            }

            for (int i = 0; i < count; i++)
            {
                simulation.Entities.SetHealth(_victims[i], new HealthComponent(0f, 1f));
                simulation.DespawnEntity(_victims[i]);
            }

            for (int t = 0; t < CycleTicks; t++)
            {
                harness.Step();
            }

            this.Respawns[_runs++] = simulation.Entities.GetPool<MonsterComponent>().EntitySlots.Length;
        }
    }
}
