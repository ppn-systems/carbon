// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Combat.Attacks;
using Carbon.Combat.Components;
using Carbon.Combat.Events;
using Carbon.Combat.Projectiles;
using Carbon.Combat.Statuses;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Combat.Tests;

public sealed class AttackSystemTests
{
    private const float Dt = 0.05f;
    private const uint VictimLayer = 2;

    private static readonly AttackProfile s_melee = new()
    {
        Kind = AttackKind.Melee,
        MinRange = 0f,
        MaxRange = 30f,
        WindupTicks = 4,
        TravelTicks = 2,
        ActiveTicks = 3,
        RecoveryTicks = 5,
        CooldownTicks = 30,
        DamageMultiplier = 1f,
        AreaRadius = 26f,
        HalfAngleRadians = MathF.PI / 3f,
        LungeDistance = 4f
    };

    private static readonly AttackProfile s_projectile = new()
    {
        Kind = AttackKind.Projectile,
        MinRange = 0f,
        MaxRange = 200f,
        WindupTicks = 3,
        ActiveTicks = 1,
        RecoveryTicks = 2,
        CooldownTicks = 20,
        DamageMultiplier = 1f,
        ProjectileSpeed = 400f,
        ProjectileRadius = 2f,
        ArcHeight = 12f
    };

    private static readonly AttackProfile s_spell = new()
    {
        Kind = AttackKind.Spell,
        MinRange = 0f,
        MaxRange = 200f,
        WindupTicks = 5,
        TravelTicks = 4,
        ActiveTicks = 1,
        RecoveryTicks = 3,
        CooldownTicks = 40,
        DamageMultiplier = 1f,
        AreaRadius = 30f,
        Shape = AttackAreaShape.Circle
    };

    private static readonly AttackProfile s_leap = new()
    {
        Kind = AttackKind.Leap,
        MinRange = 20f,
        MaxRange = 100f,
        WindupTicks = 3,
        TravelTicks = 5,
        ActiveTicks = 2,
        RecoveryTicks = 4,
        CooldownTicks = 40,
        DamageMultiplier = 1f,
        AreaRadius = 24f
    };

    [Fact]
    public void TryBeginAttack_MeleeTimeline_EmitsWindupLungeActiveRecoveryAtExactTicks()
    {
        Harness h = new(s_melee);
        Entity victim = h.SpawnVictim(new Vector2(20f, 0f));

        h.Begin(0, victim);
        h.Run(20);

        h.PhaseTicks.Should().Equal(
            [(0, AttackPhase.Windup), (4, AttackPhase.Travel), (6, AttackPhase.Active), (9, AttackPhase.Recovery), (14, AttackPhase.None)],
            "the melee timeline is windup(4) -> lunge(2) -> active(3) -> recovery(5)");
        h.PhaseEvents[0].Kind.Should().Be(AttackKind.Melee);
        h.PhaseEvents[0].Progress.Should().Be(0f);
        h.PhaseEvents[2].Progress.Should().BeApproximately(6f / 14f, 1e-5f, "progress is the normalized position in the whole timeline");
        h.PhaseEvents[^1].Progress.Should().Be(1f);
        h.PhaseEvents[^1].Interrupted.Should().BeFalse();
    }

    [Fact]
    public void Update_MeleeTargetInReachThroughout_HitsOnlyDuringActiveFramesAndOnce()
    {
        Harness h = new(s_melee);
        Entity victim = h.SpawnVictim(new Vector2(18f, 0f));

        h.Begin(0, victim);
        h.Run(20);

        h.DamageTicks.Should().ContainSingle("a target is struck at most once per swing");
        h.DamageTicks[0].Should().BeInRange(6, 8, "damage resolves only inside the active window (ticks 6..8)");
        h.World.Entities.GetHealth(victim).Current.Should().BeApproximately(90f, 1e-3f);
    }

    [Fact]
    public void Update_MeleeTargetLeavesBeforeActiveFrames_DealsNoDamage()
    {
        Harness h = new(s_melee);
        Entity victim = h.SpawnVictim(new Vector2(18f, 0f));

        h.Begin(0, victim);
        h.Run(3); // still winding up
        h.World.EnqueueCommand(SimulationCommand.Teleport(victim, new Vector2(120f, 0f)));
        h.Run(17);

        h.DamageTicks.Should().BeEmpty("the wind-up is a telegraph: dodging before the active frames avoids the hit");
    }

    [Fact]
    public void Update_ProjectileTimeline_AimsReleasesProjectileWithoutTravelPhaseAndProjectileHits()
    {
        Harness h = new(s_projectile, withProjectiles: true);
        Entity victim = h.SpawnVictim(new Vector2(150f, 0f));

        h.Begin(0, victim);
        h.Run(1);
        h.World.EnqueueCommand(SimulationCommand.Teleport(victim, new Vector2(150f, 40f))); // aim tracks during windup
        h.Run(15);

        h.PhaseTicks.Should().Equal(
            [(0, AttackPhase.Windup), (3, AttackPhase.Active), (4, AttackPhase.Recovery), (6, AttackPhase.None)],
            "projectiles have no travel phase: aim(3) -> release(1) -> recovery(2)");
        h.Launches.Should().ContainSingle();
        h.Launches[0].ArcHeight.Should().Be(12f);
        Vector2 dir = Vector2.Normalize(h.Launches[0].Velocity);
        dir.Y.Should().BeGreaterThan(0.2f, "the aim followed the target before release");
        h.DamageTicks.Should().ContainSingle("the released projectile is resolved by ProjectileSystem");
        h.DamageTicks[0].Should().BeGreaterThan(3);
    }

    [Fact]
    public void Update_SpellTimeline_DetonatesAtLockedTelegraphAfterDelay()
    {
        Harness h = new(s_spell);
        Entity dodger = h.SpawnVictim(new Vector2(100f, 0f));
        Entity bystander = h.SpawnVictim(new Vector2(160f, 0f));

        h.Begin(0, dodger);
        h.Run(2);
        h.World.EnqueueCommand(SimulationCommand.Teleport(dodger, new Vector2(100f, 120f)));
        h.World.EnqueueCommand(SimulationCommand.Teleport(bystander, new Vector2(110f, 5f)));
        h.Run(15);

        h.PhaseTicks.Should().Equal(
            [(0, AttackPhase.Windup), (5, AttackPhase.Travel), (9, AttackPhase.Active), (10, AttackPhase.Recovery), (13, AttackPhase.None)],
            "spell timeline is cast(5) -> delayed detonation(4) -> detonate(1) -> recovery(3)");
        h.PhaseEvents[0].TargetPosition.Should().Be(new Vector2(100f, 0f), "the ground telegraph is locked when the cast starts");
        h.PhaseEvents[0].AreaRadius.Should().Be(30f);
        h.World.Entities.GetHealth(dodger).Current.Should().Be(100f, "the original target left the telegraph");
        h.World.Entities.GetHealth(bystander).Current.Should().BeLessThan(100f, "whoever stands in the circle at detonation is hit");
        h.DamageTicks.Should().Equal([9]);
    }

    [Fact]
    public void Update_LeapTimeline_TravelsToLockedLandingPointAndHitsOnLanding()
    {
        Harness h = new(s_leap);
        Entity victim = h.SpawnVictim(new Vector2(60f, 0f));

        h.Begin(0, victim);
        h.Run(20);

        h.PhaseTicks.Should().Equal(
            [(0, AttackPhase.Windup), (3, AttackPhase.Travel), (8, AttackPhase.Active), (10, AttackPhase.Recovery), (14, AttackPhase.None)],
            "leap timeline is crouch(3) -> airborne(5) -> landing(2) -> recovery(4)");
        h.World.Entities.GetTransform(h.Attacker).Position.X.Should().BeGreaterThan(35f, "the attacker flew toward the landing point");
        h.DamageTicks.Should().Equal([8]);
    }

    [Fact]
    public void Update_AttackerStunnedMidWindup_InterruptsWithNoneEvent()
    {
        Harness h = new(s_melee);
        Entity victim = h.SpawnVictim(new Vector2(18f, 0f));

        h.Begin(0, victim);
        h.Run(2);
        h.World.Entities.SetComponent(h.Attacker, new CombatStateComponent(CrowdControlMask.Stunned));
        h.Run(10);

        h.PhaseEvents[^1].Phase.Should().Be(AttackPhase.None);
        h.PhaseEvents[^1].Interrupted.Should().BeTrue();
        h.DamageTicks.Should().BeEmpty();
    }

    [Fact]
    public void TryBeginAttack_WhileOnCooldown_ReturnsFalse()
    {
        Harness h = new(s_melee);
        Entity victim = h.SpawnVictim(new Vector2(18f, 0f));

        h.Begin(0, victim).Should().BeTrue();
        h.Run(20);
        h.Begin(0, victim).Should().BeFalse("the 30-tick cooldown has not elapsed at tick 20");
        h.Run(10);
        h.Begin(0, victim).Should().BeTrue("the cooldown elapsed at tick 30");
    }

    [Fact]
    public void Update_WithHostEffects_RoutesHitsAndProjectileLaunchesThroughHost()
    {
        RecordingEffects effects = new();
        Harness melee = new(s_melee, effects: effects);
        Entity victim = melee.SpawnVictim(new Vector2(18f, 0f));
        melee.Begin(0, victim);
        melee.Run(20);

        Harness ranged = new(s_projectile with { ProjectileVisual = 5 }, effects: effects);
        Entity target = ranged.SpawnVictim(new Vector2(150f, 0f));
        ranged.Begin(0, target);
        ranged.Run(10);

        effects.Hits.Should().Equal([(victim, 10f)], "the host applies the single melee hit");
        melee.World.Entities.GetHealth(victim).Current.Should().Be(100f, "the default damage path is bypassed");
        effects.Launches.Should().ContainSingle().Which.VisualArchetype.Should().Be(5);
        ranged.Launches.Should().ContainSingle().Which.Projectile.Should().Be(Entity.Null, "the host chose not to spawn an entity");
    }

    private sealed class RecordingEffects : IAttackEffects
    {
        public List<(Entity Victim, float Damage)> Hits { get; } = [];

        public List<ProjectileLaunchRequest> Launches { get; } = [];

        public bool ApplyHit(WorldSimulation world, Entity attacker, Entity victim, float damage)
        {
            this.Hits.Add((victim, damage));
            return true;
        }

        public Entity LaunchProjectile(WorldSimulation world, in ProjectileLaunchRequest request)
        {
            this.Launches.Add(request);
            return Entity.Null;
        }
    }

    private sealed class Harness
    {
        private readonly AttackSystem _attacks;
        private ulong _tick;

        public Harness(AttackProfile profile, bool withProjectiles = false, IAttackEffects? effects = null)
        {
            this.World = new WorldSimulation(tickRate: 20);
            _attacks = new AttackSystem([profile], effects);
            this.World.RegisterSystem(_attacks);
            if (withProjectiles)
            {
                this.World.RegisterSystem(new ProjectileSystem());
            }

            this.Attacker = this.World.SpawnEntity(Vector2.Zero, radius: 6f);
            this.World.Entities.SetCollider(this.Attacker, Collider2D.CreateCircle(6f) with { Layer = 4 });
            this.World.Entities.SetVelocity(this.Attacker, new Velocity2D(Vector2.Zero));
        }

        public WorldSimulation World { get; }

        public Entity Attacker { get; }

        public List<AttackPhaseChangedEvent> PhaseEvents { get; } = [];

        public List<(int Tick, AttackPhase Phase)> PhaseTicks { get; } = [];

        public List<int> DamageTicks { get; } = [];

        public List<ProjectileLaunchedEvent> Launches { get; } = [];

        public Entity SpawnVictim(Vector2 position)
        {
            Entity victim = this.World.SpawnEntity(position, radius: 5f);
            this.World.Entities.SetCollider(victim, Collider2D.CreateCircle(5f) with { Layer = VictimLayer });
            this.World.Entities.SetHealth(victim, new HealthComponent(100f, 100f));
            return victim;
        }

        public bool Begin(ushort profileId, Entity target)
        {
            // Called before the tick runs: the start tick is the first windup tick.
            Vector2 aim = this.World.Entities.GetTransform(target).Position;
            return _attacks.TryBeginAttack(this.World, this.Attacker, profileId, target, aim, 10f, VictimLayer, _tick);
        }

        public void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                this.World.Tick(new FixedTickArgs(_tick, Dt, _tick * Dt));
                int tick = (int)_tick;
                foreach (AttackPhaseChangedEvent e in this.World.ReadEvents<AttackPhaseChangedEvent>())
                {
                    this.PhaseEvents.Add(e);
                    this.PhaseTicks.Add((tick, e.Phase));
                }

                for (int d = 0; d < this.World.ReadEvents<DamageEvent>().Length; d++)
                {
                    this.DamageTicks.Add(tick);
                }

                this.Launches.AddRange(this.World.ReadEvents<ProjectileLaunchedEvent>());
                this.World.ClearAllEvents();
                _tick++;
            }
        }
    }
}
