// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.AI.Components;
using Carbon.Combat.Attacks;
using Carbon.Combat.Projectiles;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;
using Carbon.Creatures.Spawning;
using Carbon.Maps.Biomes;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Creatures.Tests;

public sealed class MonsterBehaviorTests
{
    private const double TickSeconds = 1.0 / 30.0;

    [Fact]
    public void Update_RangedSkeletonWithPlayerTooClose_KitesBackAndShoots()
    {
        Arena arena = new();
        Entity player = arena.SpawnPlayer(new Vector2(60f, 0f));
        Entity skeleton = arena.SpawnMonster("skeleton_archer", Vector2.Zero);

        int launches = 0;
        for (int i = 0; i < 150; i++)
        {
            arena.Step();
            launches += arena.World.ReadEvents<ProjectileLaunchedEvent>().Length;
        }

        float dist = arena.Distance(skeleton, player);
        dist.Should().BeGreaterThanOrEqualTo(100f, "ranged monsters back off to their retreat range (110)");
        dist.Should().BeLessThanOrEqualTo(260f, "ranged monsters stay inside their engage band");
        launches.Should().BeGreaterThan(0, "the skeleton fires projectiles");
        arena.World.Entities.GetHealth(player).Current.Should().BeLessThan(Arena.PlayerHealth, "projectiles hit the player");
    }

    [Fact]
    public void Update_CasterDemonWithPlayerTooClose_HoldsMidDistance()
    {
        Arena arena = new();
        Entity player = arena.SpawnPlayer(new Vector2(30f, 0f));
        Entity demon = arena.SpawnMonster("orc_shaman", Vector2.Zero);

        for (int i = 0; i < 200; i++)
        {
            arena.Step();
        }

        float dist = arena.Distance(demon, player);
        dist.Should().BeInRange(70f, 210f, "casters keep mid distance (retreat 80, engage 180)");
    }

    [Fact]
    public void Update_MeleeOrcWithDistantPlayer_ClosesInAndHits()
    {
        Arena arena = new();
        Entity player = arena.SpawnPlayer(new Vector2(150f, 0f));
        Entity orc = arena.SpawnMonster("rock_golem", Vector2.Zero);

        bool sawMelee = false;
        for (int i = 0; i < 150; i++)
        {
            arena.Step();
            foreach (AttackPhaseChangedEvent e in arena.World.ReadEvents<AttackPhaseChangedEvent>())
            {
                sawMelee |= e.Attacker == orc && e.Kind == AttackKind.Melee && e.Phase == AttackPhase.Active;
            }
        }

        arena.Distance(orc, player).Should().BeLessThan(45f, "melee monsters close in");
        sawMelee.Should().BeTrue();
        arena.World.Entities.GetHealth(player).Current.Should().BeLessThan(Arena.PlayerHealth);
    }

    [Fact]
    public void Update_MonsterDraggedPastLeashMidAttack_InterruptsAttackAndReturnsHome()
    {
        Arena arena = new();
        _ = arena.SpawnPlayer(new Vector2(20f, 0f));
        Entity orc = arena.SpawnMonster("rock_golem", Vector2.Zero);

        for (int i = 0; i < 60 && !arena.World.Entities.GetComponent<AttackStateComponent>(orc).IsBusy; i++)
        {
            arena.Step();
        }

        arena.World.Entities.GetComponent<AttackStateComponent>(orc).IsBusy.Should().BeTrue("the orc started an attack");
        arena.World.EnqueueCommand(SimulationCommand.Teleport(orc, new Vector2(900f, 0f)));

        bool interrupted = false;
        for (int i = 0; i < 3; i++)
        {
            arena.Step();
            foreach (AttackPhaseChangedEvent e in arena.World.ReadEvents<AttackPhaseChangedEvent>())
            {
                interrupted |= e.Attacker == orc && e.Phase == AttackPhase.None && e.Interrupted;
            }
        }

        interrupted.Should().BeTrue("leashing cancels the attack in progress");
        arena.World.Entities.GetComponent<AiBrainComponent>(orc).State.Should().Be(AiState.ReturnHome);
    }

    [Fact]
    public void Update_SteadyStateCombatTicks_AllocateNothing()
    {
        Arena arena = new();
        Entity player = arena.SpawnPlayer(new Vector2(20f, 0f), health: 1e6f);
        _ = arena.SpawnMonster("rock_golem", Vector2.Zero);
        _ = arena.SpawnMonster("wild_slime", new Vector2(80f, 30f));
        _ = arena.SpawnMonster("crimson_demon", new Vector2(-60f, -40f));

        // Step world.Tick directly: WorldSimulation.Update allocates a method-group delegate per call.
        for (int i = 0; i < 400; i++)
        {
            arena.StepTick();
        }

        float healthBefore = arena.World.Entities.GetHealth(player).Current;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 300; i++)
        {
            arena.StepTick();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.Should().Be(0, "AI, monster brain, and melee/leap attack ticks are zero-allocation");
        arena.World.Entities.GetHealth(player).Current.Should().BeLessThan(healthBefore, "the measured window contains resolved attacks");
    }

    private sealed class Arena
    {
        public const float PlayerHealth = 5000f;

        private readonly MonsterCatalog _catalog = CreatureTestData.LoadCatalog();
        private readonly SpawningConfig _config = CreatureTestData.LoadSpawning();
        private ulong _tick;

        public Arena()
        {
            this.World = new WorldSimulation(tickRate: 30);
            this.Systems = CreatureSystems.Register(
                this.World,
                _catalog,
                _config,
                new FixedBiomeSampler(BiomeType.LushPlains),
                Vector2.Zero,
                seed: 7UL);
            this.World.RegisterSystem(new ProjectileSystem());
        }

        public WorldSimulation World { get; }

        public CreatureSystems Systems { get; }

        public Entity SpawnPlayer(Vector2 position, float health = PlayerHealth)
        {
            Entity player = this.World.SpawnEntity(position, radius: 6f);
            this.World.Entities.SetCollider(player, Collider2D.CreateCircle(6f) with { Layer = _config.TargetLayerMask });
            this.World.Entities.SetHealth(player, new HealthComponent(health, health));
            return player;
        }

        public Entity SpawnMonster(string id, Vector2 position)
        {
            ushort index = _catalog.GetIndex(id);
            ScaledMonsterStats stats = this.Systems.Spawner.Scaling.Compute(0f, isNight: false, _catalog.GetDefinition(index));
            return MonsterFactory.Spawn(this.World, _catalog, index, position, in stats, _config.MonsterLayer, _config.TargetLayerMask);
        }

        public void Step() => _ = this.World.Update(TickSeconds);

        public void StepTick()
        {
            this.World.ClearAllEvents();
            this.World.Tick(new FixedTickArgs(_tick, (float)TickSeconds, _tick * TickSeconds));
            _tick++;
        }

        public float Distance(Entity a, Entity b) =>
            Vector2.Distance(this.World.Entities.GetTransform(a).Position, this.World.Entities.GetTransform(b).Position);
    }
}
