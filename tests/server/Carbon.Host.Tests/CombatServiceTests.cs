// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Abstractions.Persistence;
using Carbon.Abstractions.World;
using Carbon.AI.Components;
using Carbon.AI.Threat;
using Carbon.Application.World.Combat;
using Carbon.Application.World.Players;
using Carbon.Application.World.Replication;
using Carbon.Combat.Components;
using Carbon.Combat.Damage;
using Carbon.Combat.Stats;
using Carbon.Combat.Statuses;
using Carbon.Items.Containers;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using FluentAssertions;
using Xunit;

namespace Carbon.Host.Tests;

public sealed class CombatServiceTests
{
    private const uint MonsterLayer = 2U;
    private const ulong Viewer = 77UL;

    [Fact]
    public void RollCriticalHit_IsDeterministicAndBoundedByTheChance()
    {
        CombatService.RollCriticalHit(0f, 123U).Should().BeFalse();
        CombatService.RollCriticalHit(1f, 123U).Should().BeTrue();
        CombatService.RollCriticalHit(0.5f, 99U).Should().Be(CombatService.RollCriticalHit(0.5f, 99U));
    }

    [Fact]
    public void ApplyMonsterHits_SurvivingMonster_TakesDamageAggroesAndIsKnockedBack()
    {
        Harness h = new();
        Entity mob = h.SpawnMonster(500UL, new Vector2(20f, 0f), health: 50f);

        bool damaged = h.Combat.ApplyMonsterHits(h.Player, Vector2.Zero, Vector2.UnitX, [mob], 10f, isCritical: true, knockbackSpeed: 135f);

        damaged.Should().BeTrue();
        h.Simulation.Entities.GetHealth(mob).Current.Should().Be(40f);
        h.Simulation.Entities.GetComponent<AiBrainComponent>(mob).State.Should().Be(AiState.Chase);
        h.Simulation.Entities.GetComponent<AiBrainComponent>(mob).TargetEntity.Should().Be(h.Player.Entity);
        h.Simulation.Entities.GetVelocity(mob).Linear.Should().Be(new Vector2(135f, 0f));
        h.Simulation.Entities.GetAttachmentStore<ThreatTable>().TryGet(mob, out _).Should().BeTrue();
        OutboundCombatHit hit = h.Outbound.Pending.CombatHits.Should().ContainSingle().Subject;
        hit.DefenderNetworkId.Should().Be(500UL);
        hit.Damage.Should().Be(10f);
        hit.IsCritical.Should().BeTrue();
        hit.IsKilled.Should().BeFalse();
        h.Kills.Should().BeEmpty();
    }

    [Fact]
    public void ApplyMonsterHits_InvulnerableMonster_TakesNoDamage()
    {
        Harness h = new();
        Entity mob = h.SpawnMonster(500UL, new Vector2(20f, 0f), health: 50f);
        h.Simulation.Entities.SetComponent(mob, new CombatStateComponent(CrowdControlMask.Invulnerable));

        bool damaged = h.Combat.ApplyMonsterHits(h.Player, Vector2.Zero, Vector2.UnitX, [mob], 10f, isCritical: false, knockbackSpeed: 0f);

        damaged.Should().BeFalse("player hits go through CombatDamage.TryResolve, which honours invulnerability (audit DOM-1)");
        h.Simulation.Entities.GetHealth(mob).Current.Should().Be(50f);
        h.Outbound.Pending.CombatHits.Should().BeEmpty();
    }

    [Fact]
    public void ApplyMonsterHits_ArmoredMonster_UsesTheSharedArmorMitigation()
    {
        Harness h = new();
        Entity mob = h.SpawnMonster(500UL, new Vector2(20f, 0f), health: 500f);
        StatSheet sheet = h.Simulation.Entities.GetAttachmentStore<StatSheet>().GetOrAdd(mob, static () => new StatSheet());
        sheet.SetBase(StatType.Defense, 100f);
        float expected = new ArmorDamageMitigation().Mitigate(h.Simulation, mob, 100f);

        _ = h.Combat.ApplyMonsterHits(h.Player, Vector2.Zero, Vector2.UnitX, [mob], 100f, isCritical: false, knockbackSpeed: 0f);

        expected.Should().BeApproximately(50f, 1e-3f);
        h.Simulation.Entities.GetHealth(mob).Current.Should().BeApproximately(500f - expected, 1e-3f);
    }

    [Fact]
    public void ApplyMonsterHits_DealingDamage_MarksTheAttackerInCombat()
    {
        Harness h = new() { Tick = 4321UL };
        Entity mob = h.SpawnMonster(500UL, new Vector2(20f, 0f), health: 50f);

        _ = h.Combat.ApplyMonsterHits(h.Player, Vector2.Zero, Vector2.UnitX, [mob], 10f, isCritical: false, knockbackSpeed: 0f);

        h.Player.LastCombatDamageTick.Should().Be(4321UL, "attacking pauses out-of-combat regeneration just like being hit");
    }

    [Fact]
    public void ApplyMonsterHits_WallInTheWay_LeavesTheAttackerOutOfCombat()
    {
        Harness h = new() { Tick = 4321UL };
        Entity mob = h.SpawnMonster(500UL, new Vector2(20f, 0f), health: 50f);
        h.Physics.WallHit = true;

        _ = h.Combat.ApplyMonsterHits(h.Player, Vector2.Zero, Vector2.UnitX, [mob], 10f, isCritical: false, knockbackSpeed: 0f);

        h.Player.LastCombatDamageTick.Should().Be(0UL);
    }

    [Fact]
    public void ApplyMonsterHits_IdlePackmateNearby_JoinsTheChase()
    {
        Harness h = new();
        Entity mob = h.SpawnMonster(500UL, new Vector2(20f, 0f), health: 50f);
        Entity ally = h.SpawnMonster(501UL, new Vector2(60f, 0f), health: 50f);

        _ = h.Combat.ApplyMonsterHits(h.Player, Vector2.Zero, Vector2.UnitX, [mob], 10f, isCritical: false, knockbackSpeed: 0f);

        h.Simulation.Entities.GetComponent<AiBrainComponent>(ally).State.Should().Be(AiState.Chase);
        h.Simulation.Entities.GetHealth(ally).Current.Should().Be(50f, "packmates are alerted, not hit");
    }

    [Fact]
    public void ApplyMonsterHits_LethalHit_RunsTheKillHandlerThenDespawns()
    {
        Harness h = new();
        Entity mob = h.SpawnMonster(500UL, new Vector2(20f, 0f), health: 5f);

        _ = h.Combat.ApplyMonsterHits(h.Player, Vector2.Zero, Vector2.UnitX, [mob], 10f, isCritical: false, knockbackSpeed: 135f);

        h.Kills.Should().Equal((500UL, new Vector2(20f, 0f)));
        h.Simulation.Entities.IsAlive(mob).Should().BeFalse();
        h.Outbound.Pending.CombatHits.Should().ContainSingle().Which.IsKilled.Should().BeTrue();
    }

    [Fact]
    public void ApplyMonsterHits_WallInTheWay_DoesNotHit()
    {
        Harness h = new();
        h.Physics.WallHit = true;
        Entity mob = h.SpawnMonster(500UL, new Vector2(40f, 0f), health: 50f);

        h.Combat.ApplyMonsterHits(h.Player, Vector2.Zero, Vector2.UnitX, [mob], 10f, false, 135f).Should().BeFalse();

        h.Simulation.Entities.GetHealth(mob).Current.Should().Be(50f);
        h.Outbound.Pending.CombatHits.Should().BeEmpty();
    }

    [Fact]
    public void QueueSkillCast_PlayersNearby_BroadcastsTheCastAimedSixtyFourPixelsOut()
    {
        Harness h = new();

        h.Combat.QueueSkillCast(new Vector2(10f, 20f), Vector2.UnitY, 900UL, 3);

        OutboundSkillCast cast = h.Outbound.Pending.SkillCasts.Should().ContainSingle().Subject;
        (cast.AimX, cast.AimY).Should().Be((10f, 84f));
        h.Outbound.Pending.Recipients[cast.RecipientsOffset].Should().Be(Viewer);
    }

    [Fact]
    public void QueueCombatHit_NobodyNearby_SendsNothing()
    {
        Harness h = new() { NobodyNearby = true };

        h.Combat.QueueCombatHit(Vector2.Zero, 1UL, 2UL, 5f, 10f, false, false);

        h.Outbound.Pending.CombatHits.Should().BeEmpty();
        h.Outbound.Pending.Recipients.Should().BeEmpty();
    }

    private sealed class Harness
    {
        public Harness()
        {
            this.Combat = new CombatService(
                this.Simulation,
                this.Physics,
                MonsterLayer,
                this.Outbound,
                (_, output) =>
                {
                    output.Clear();
                    if (!this.NobodyNearby)
                    {
                        output.Add(Viewer);
                    }
                },
                (_, netId, pos) => this.Kills.Add((netId, pos)),
                () => this.Tick);

            ConnectedPlayerSession session = new(1UL, 9UL, Guid.NewGuid(), Guid.NewGuid(), "Tester", 900UL, 0, 0);
            CharacterRecord character = new(
                session.CharacterId, session.AccountId, "Tester", 1, 0, 0f, 0f, 100f, 100f, 0, [], DateTimeOffset.UnixEpoch);
            this.Player = new PlayerState(session, this.Simulation.SpawnNetworkEntity(900UL, Vector2.Zero, 7f), character, new InventoryContainer(4));
        }

        public WorldSimulation Simulation { get; } = new();

        public ulong Tick { get; set; } = 1UL;

        public FakePhysics Physics { get; } = new();

        public OutboundTickBuffer Outbound { get; } = new();

        public CombatService Combat { get; }

        public PlayerState Player { get; }

        public bool NobodyNearby { get; set; }

        public List<(ulong NetworkId, Vector2 Position)> Kills { get; } = [];

        public Entity SpawnMonster(ulong netId, Vector2 position, float health)
        {
            Entity mob = this.Simulation.SpawnNetworkEntity(netId, position, 8f);
            this.Simulation.Entities.SetCollider(mob, new Collider2D(ColliderKind.Circle, 8f, default, false, MonsterLayer, 0U));
            this.Simulation.Entities.SetHealth(mob, new HealthComponent(health, health));
            this.Simulation.Entities.SetVelocity(mob, new Velocity2D(0f, 0f, 0f));
            this.Simulation.Entities.SetComponent(mob, new AiBrainComponent(AiState.Idle, position, 100f, 20f, 300f, 50f));
            return mob;
        }
    }

    private sealed class FakePhysics : IWorldPhysics2D
    {
        public bool WallHit { get; set; }

        public float GetMoveSpeedMultiplier(Vector2 worldPosition) => 1f;

        public bool ResolveCircleCollision(ref Vector2 position, float radius, out Vector2 pushNormal)
        {
            pushNormal = Vector2.Zero;
            return false;
        }

        public bool ResolveAabbCollision(ref Vector2 position, in AABB2D localBox, out Vector2 pushNormal)
        {
            pushNormal = Vector2.Zero;
            return false;
        }

        public bool Raycast(in Ray2D ray, out RaycastHit2D hit)
        {
            hit = default;
            return this.WallHit;
        }
    }
}
