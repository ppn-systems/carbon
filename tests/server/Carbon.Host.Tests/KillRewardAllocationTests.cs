// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Abstractions.World;
using Carbon.Application.World;
using Carbon.Creatures.Components;
using Carbon.Economy;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Serialization;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Carbon.Testing;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Carbon.Host.Tests;

/// <summary>
/// Audit round 3, ALLOC-13: a player's killing hit runs the kill rewards (<c>OnMonsterKilledLocked</c>: the loot roll,
/// ground loot, experience). The tick guards never kill, and the respawn guard (ADR-044) kills by despawn only, so this
/// guard kills monsters through the real attack path, with the shipped economy, and measures the bytes per kill.
/// </summary>
public sealed class KillRewardAllocationTests(ITestOutputHelper output)
{
    /// <summary>
    /// Ticks between kill rounds. A kill's cell respawns its monster 1800 ticks later, so after the warm-up each round
    /// kills about as many monsters as have respawned since the last (about 4), and respawns, pickups and loot expiry
    /// all land inside the rounds.
    /// </summary>
    private const int RoundTicks = 240;

    /// <summary>Hotbar slot of the starter sword.</summary>
    private const byte SwordSlot = 2;

    /// <summary>
    /// Rounds per window (warm-up, measured, repeat). The warm-up grows the loot, entity and replication capacities to the
    /// peak number of stacks on the ground, and tiers up the swing and pickup code.
    /// </summary>
    private const int Rounds = 24;

    [Fact]
    public void MonsterKill_SteadyState_BytesPerKill()
    {
        string configDir = GameEconomy.FindConfigDirectory(AppContext.BaseDirectory)
            ?? throw new InvalidOperationException("the host ships resources/config next to its binaries");
        // Every player holds the starter sword (hotbar slot 2), so the swing takes the same path from the first round.
        InventoryContainer bag = new(PlayerInventoryLayout.TotalCapacity);
        StarterGameCatalog.GrantStarterLoadout(bag);
        using TickAllocationHarness harness = TickAllocationHarness.Create(
            players: 8,
            economy: GameEconomy.LoadFromDirectory(configDir),
            inventoryBlob: InventoryBinaryCodec.Serialize(bag));
        for (int i = 0; i < 900; i++)
        {
            harness.Step();
        }

        Killer killer = new(harness);
        AllocationResult window = AllocationProbe.Measure(Rounds, warmupIterations: Rounds, _ => killer.Round());
        int kills = killer.Kills(from: Rounds, count: Rounds); // the rounds after the warm-up: the first window
        int repeatKills = killer.Kills(from: 2 * Rounds, count: Rounds);
        output.WriteLine($"{(double)window.Bytes / Math.Max(1, kills):F1} B per kill over {kills} kills, " +
            $"{(double)window.RepeatBytes!.Value / Math.Max(1, repeatKills):F1} over {repeatKills} in the repeat window ({window})");

        kills.Should().BeGreaterThanOrEqualTo(24, "the measured rounds killed monsters through the attack path");
        window.Bytes.Should().BeGreaterThan(0, $"characterisation: kill rewards allocate today ({window})");
    }

    /// <summary>
    /// Each round: up to one live monster per player is moved beside it at 1 health, the player attacks it, and the world
    /// ticks on with the players standing still.
    /// </summary>
    private sealed class Killer(TickAllocationHarness harness)
    {
        private readonly int[] _killsByRound = new int[3 * Rounds];
        private int _round;

        public int Kills(int from, int count)
        {
            int total = 0;
            for (int i = from; i < from + count; i++)
            {
                total += _killsByRound[i];
            }

            return total;
        }

        public void Round()
        {
            AuthoritativeWorld world = harness.World;
            WorldSimulation simulation = world.Simulation;
            ComponentPool<MonsterComponent> monsters = simulation.Entities.GetPool<MonsterComponent>();
            int kills = 0;
            int victimSlot = 0;
            Span<Entity> victims = stackalloc Entity[8];
            Span<ulong> victimIds = stackalloc ulong[8];
            int count = Math.Min(harness.Connections.Count, monsters.EntitySlots.Length);
            for (int i = 0; i < count; i++)
            {
                victims[i] = simulation.Entities.GetEntity(monsters.EntitySlots[i]);
            }

            for (int i = 0; i < count; i++)
            {
                Entity victim = victims[i];
                if (!world.TryGetSessionByConnection(harness.Connections[i], out ConnectedPlayerSession? session) ||
                    !simulation.TryGetEntityByNetworkId(session!.PlayerNetworkId, out Entity player) ||
                    !simulation.TryGetNetworkIdentity(victim, out NetworkIdentity identity))
                {
                    continue;
                }

                Vector2 at = simulation.Entities.GetTransform(player).Position + new Vector2(14f, 0f);
                simulation.Entities.SetTransform(victim, new Transform2D(at));
                HealthComponent health = simulation.Entities.GetHealth(victim);
                simulation.Entities.SetHealth(victim, new HealthComponent(1f, health.Max));
                victimIds[victimSlot++] = identity.NetworkId;
                _ = world.EnqueueAttackInput(harness.Connections[i], identity.NetworkId, at, SwordSlot);
            }

            world.StepFixedTick(); // the swings land on this tick
            for (int i = 0; i < victimSlot; i++)
            {
                if (!simulation.TryGetEntityByNetworkId(victimIds[i], out Entity e) || !simulation.Entities.IsAlive(e))
                {
                    kills++;
                }
            }

            // The players stand still, so chunk streaming (ALLOC-12, its own guard) stays out of the measurement.
            for (int t = 0; t < RoundTicks; t++)
            {
                world.StepFixedTick();
            }

            _killsByRound[_round++] = kills;
        }
    }
}
