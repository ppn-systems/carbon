// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.S2C;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Networking.Session;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>
/// Behaviour tests for the replication rules that moved from <c>MultiplayerSession</c>'s packet handlers onto
/// <see cref="RemoteEntityState"/> (audit round 2, SRP-1 / OOP): equipment, attack phases and delta-batch rows, including
/// the sanitising of non-finite or out-of-range server input.
/// </summary>
public sealed class RemoteEntityStateTests
{
    private const float Precision = 1e-5f;

    [Fact]
    public void FromSpawn_CopiesTheSpawnAndAppliesItsDefaults()
    {
        using EntitySpawn spawn = new()
        {
            NetworkId = 42,
            Kind = EntityKind.Monster,
            ArchetypeId = 7,
            Name = "Wolf",
            PositionX = 10f,
            PositionY = -4f,
            VelocityX = 1f,
            VelocityY = 2f,
            Radius = 0f,
            CurrentHealth = 30f,
            MaxHealth = 40f,
            Level = 0,
            LootProtectionMs = 2500,
        };

        RemoteEntityState state = RemoteEntityState.FromSpawn(spawn, "entity.wolf");

        state.NetworkId.Should().Be(42UL);
        state.NameKey.Should().Be("entity.wolf");
        state.Position.Should().Be(new Vector2(10f, -4f));
        state.Velocity.Should().Be(new Vector2(1f, 2f));
        state.Radius.Should().Be(7f, "a missing radius falls back to the default body size");
        state.Level.Should().Be(1, "level 0 is shown as 1");
        state.LootProtectionRemaining.Should().BeApproximately(2.5f, Precision);
        state.FacingDir.Should().Be(new Vector2(0f, 1f));
    }

    [Fact]
    public void ApplyEquipment_CopiesGearAndClampsLevelAndMaxHealth()
    {
        RemoteEntityState state = Spawned(maxHealth: 50f);
        using EntityEquipmentChanged pkt = new()
        {
            EquippedHeadId = 1,
            EquippedChestId = 2,
            EquippedLegsId = 3,
            EquippedFeetId = 4,
            EquippedWeaponId = 5,
            WeaponUpgradeLevel = 6,
            Level = 0,
            MaxHealth = 0.25f,
        };

        state.ApplyEquipment(pkt);

        (state.EquippedHeadId, state.EquippedChestId, state.EquippedLegsId, state.EquippedFeetId, state.EquippedWeaponId)
            .Should().Be((1, 2, 3, 4, 5));
        state.WeaponUpgradeLevel.Should().Be(6);
        state.Level.Should().Be(1);
        state.MaxHealth.Should().Be(1f, "a positive max health is floored at 1");
    }

    [Fact]
    public void ApplyEquipment_ZeroMaxHealth_KeepsTheKnownMaxHealth()
    {
        RemoteEntityState state = Spawned(maxHealth: 50f);
        using EntityEquipmentChanged pkt = new() { Level = 9, MaxHealth = 0f };

        state.ApplyEquipment(pkt);

        state.MaxHealth.Should().Be(50f);
        state.Level.Should().Be(9);
    }

    [Fact]
    public void ApplyAttackPhase_FiniteDirection_FacesItAndTimesThePhaseInServerTicks()
    {
        RemoteEntityState state = Spawned();
        state.AttackPhaseElapsed = 0.7f;
        using EntityAttackPhase pkt = Phase(EntityAttackPhase.PhaseWindup, directionX: 3f, directionY: 4f);
        pkt.PhaseDurationTicks = 15;
        pkt.AttackKind = EntityAttackPhase.KindSpell;
        pkt.Shape = 2;

        state.ApplyAttackPhase(pkt, tickRate: 30);

        state.FacingDir.Should().Be(new Vector2(0.6f, 0.8f));
        state.AttackDirection.Should().Be(new Vector2(0.6f, 0.8f));
        state.AttackPhase.Should().Be(EntityAttackPhase.PhaseWindup);
        state.AttackKind.Should().Be(EntityAttackPhase.KindSpell);
        state.AttackShape.Should().Be(2);
        state.AttackPhaseElapsed.Should().Be(0f, "every phase restarts the local clock");
        state.AttackPhaseDuration.Should().BeApproximately(0.5f, Precision);
        state.AttackTarget.Should().Be(new Vector2(100f, 200f));
    }

    [Fact]
    public void ApplyAttackPhase_PhaseNone_KeepsFacingButStillRecordsTheDirection()
    {
        RemoteEntityState state = Spawned();
        Vector2 facing = state.FacingDir;
        using EntityAttackPhase pkt = Phase(EntityAttackPhase.PhaseNone, directionX: 1f, directionY: 0f);

        state.ApplyAttackPhase(pkt, tickRate: 30);

        state.FacingDir.Should().Be(facing);
        state.AttackDirection.Should().Be(Vector2.UnitX);
    }

    [Theory]
    [InlineData(float.NaN, 1f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(0.001f, 0.001f)]
    public void ApplyAttackPhase_NonFiniteOrTinyDirection_FallsBackToTheCurrentFacing(float x, float y)
    {
        RemoteEntityState state = Spawned();
        Vector2 facing = state.FacingDir;
        using EntityAttackPhase pkt = Phase(EntityAttackPhase.PhaseActive, x, y);

        state.ApplyAttackPhase(pkt, tickRate: 30);

        state.FacingDir.Should().Be(facing);
        state.AttackDirection.Should().Be(facing);
    }

    [Fact]
    public void ApplyAttackPhase_UnknownPhase_IsTreatedAsNoAttack()
    {
        RemoteEntityState state = Spawned();
        using EntityAttackPhase pkt = Phase(EntityAttackPhase.PhaseRecovery + 1, 1f, 0f);

        state.ApplyAttackPhase(pkt, tickRate: 30);

        state.AttackPhase.Should().Be(EntityAttackPhase.PhaseNone);
    }

    [Fact]
    public void ApplyAttackPhase_RecoveryPhase_IsKept()
    {
        RemoteEntityState state = Spawned();
        using EntityAttackPhase pkt = Phase(EntityAttackPhase.PhaseRecovery, 1f, 0f);

        state.ApplyAttackPhase(pkt, tickRate: 30);

        state.AttackPhase.Should().Be(EntityAttackPhase.PhaseRecovery);
    }

    [Theory]
    [InlineData(1000f, 512f)]
    [InlineData(-5f, 0f)]
    [InlineData(float.NaN, 0f)]
    [InlineData(64f, 64f)]
    public void ApplyAttackPhase_AreaRadius_IsClampedToTheRenderableRange(float sent, float expected)
    {
        RemoteEntityState state = Spawned();
        using EntityAttackPhase pkt = Phase(EntityAttackPhase.PhaseActive, 1f, 0f);
        pkt.AreaRadius = sent;

        state.ApplyAttackPhase(pkt, tickRate: 30);

        state.AttackAreaRadius.Should().Be(expected);
    }

    [Theory]
    [InlineData(10f, MathF.PI)]
    [InlineData(-1f, 0f)]
    [InlineData(float.PositiveInfinity, 0f)]
    [InlineData(0.5f, 0.5f)]
    public void ApplyAttackPhase_HalfAngle_IsClampedToHalfACircle(float sent, float expected)
    {
        RemoteEntityState state = Spawned();
        using EntityAttackPhase pkt = Phase(EntityAttackPhase.PhaseActive, 1f, 0f);
        pkt.HalfAngleRadians = sent;

        state.ApplyAttackPhase(pkt, tickRate: 30);

        state.AttackHalfAngle.Should().Be(expected);
    }

    [Fact]
    public void ApplyAttackPhase_NonFiniteTarget_AimsAtTheAttackerItself()
    {
        RemoteEntityState state = Spawned();
        using EntityAttackPhase pkt = Phase(EntityAttackPhase.PhaseActive, 1f, 0f);
        pkt.TargetY = float.NaN;

        state.ApplyAttackPhase(pkt, tickRate: 30);

        state.AttackTarget.Should().Be(state.Position);
    }

    [Fact]
    public void ApplyAttackPhase_ZeroTickRate_TreatsTheDurationAsOneTickPerSecond()
    {
        RemoteEntityState state = Spawned();
        using EntityAttackPhase pkt = Phase(EntityAttackPhase.PhaseActive, 1f, 0f);
        pkt.PhaseDurationTicks = 3;

        state.ApplyAttackPhase(pkt, tickRate: 0);

        state.AttackPhaseDuration.Should().Be(3f);
    }

    [Fact]
    public void ApplyDelta_FullRow_UpdatesTransformHealthFacingAndRecordsASnapshot()
    {
        RemoteEntityState state = Spawned();
        int before = state.Interpolation.Count;
        using EntityDeltaBatch batch = Batch(rows: 2);

        state.ApplyDelta(batch, index: 1, snapshotTime: 12.5);

        state.Position.Should().Be(new Vector2(11f, 21f));
        state.Velocity.Should().Be(new Vector2(0f, -3f));
        state.FacingDir.Should().Be(new Vector2(0f, -1f), "a velocity faster than 2 units/s turns the entity");
        state.CurrentHealth.Should().Be(81f);
        state.Interpolation.Count.Should().Be(before + 1);
        state.Interpolation.Sample(12.5).Position.Should().Be(new Vector2(11f, 21f));
    }

    [Fact]
    public void ApplyDelta_TwoBatches_InterpolateBetweenTheirServerTimes()
    {
        RemoteEntityState state = Spawned();
        using EntityDeltaBatch first = Batch(rows: 1);
        first.PositionsX = [0f];
        first.PositionsY = [0f];
        using EntityDeltaBatch second = Batch(rows: 1);
        second.PositionsX = [10f];
        second.PositionsY = [20f];

        state.ApplyDelta(first, index: 0, snapshotTime: 1.0);
        state.ApplyDelta(second, index: 0, snapshotTime: 2.0);

        Vector2 midway = state.Interpolation.Sample(1.5).Position;
        midway.X.Should().BeApproximately(5f, 1e-3f);
        midway.Y.Should().BeApproximately(10f, 1e-3f);
    }

    [Fact]
    public void ApplyDelta_SlowVelocity_KeepsTheCurrentFacing()
    {
        RemoteEntityState state = Spawned();
        Vector2 facing = state.FacingDir;
        using EntityDeltaBatch batch = Batch(rows: 1);
        batch.VelocitiesX = [1.5f];
        batch.VelocitiesY = [1.3f];

        state.ApplyDelta(batch, index: 0, snapshotTime: 1.0);

        state.Velocity.Should().Be(new Vector2(1.5f, 1.3f));
        state.FacingDir.Should().Be(facing, "|v|^2 = 3.94 is under the 4 threshold");
    }

    [Fact]
    public void ApplyDelta_ShortColumns_LeaveThoseFieldsAloneButStillRecordASnapshot()
    {
        RemoteEntityState state = Spawned(health: 33f);
        Vector2 position = state.Position;
        Vector2 velocity = state.Velocity;
        int before = state.Interpolation.Count;
        using EntityDeltaBatch batch = Batch(rows: 2);
        batch.PositionsY = [1f];
        batch.VelocitiesX = [1f];
        batch.CurrentHealths = [];

        state.ApplyDelta(batch, index: 1, snapshotTime: 3.0);

        state.Position.Should().Be(position);
        state.Velocity.Should().Be(velocity);
        state.CurrentHealth.Should().Be(33f);
        state.Interpolation.Count.Should().Be(before + 1);
    }

    [Fact]
    public void BuildAttackView_ReportsClampedProgressThroughThePhase()
    {
        RemoteEntityState state = Spawned();
        using EntityAttackPhase pkt = Phase(EntityAttackPhase.PhaseActive, 1f, 0f);
        pkt.PhaseDurationTicks = 10;
        state.ApplyAttackPhase(pkt, tickRate: 10);

        state.AttackPhaseElapsed = 0.25f;
        AttackPhaseView quarter = state.BuildAttackView();
        state.AttackPhaseElapsed = 5f;
        AttackPhaseView done = state.BuildAttackView();
        state.AttackPhaseDuration = 0f;
        AttackPhaseView instant = state.BuildAttackView();

        quarter.PhaseProgress.Should().BeApproximately(0.25f, Precision);
        done.PhaseProgress.Should().Be(1f);
        instant.PhaseProgress.Should().Be(1f);
    }

    private static RemoteEntityState Spawned(float maxHealth = 100f, float health = 100f)
    {
        using EntitySpawn spawn = new()
        {
            NetworkId = 7,
            Kind = EntityKind.Player,
            PositionX = 5f,
            PositionY = 6f,
            Radius = 8f,
            CurrentHealth = health,
            MaxHealth = maxHealth,
        };
        return RemoteEntityState.FromSpawn(spawn, nameKey: null);
    }

    private static EntityAttackPhase Phase(byte phase, float directionX, float directionY) => new()
    {
        Phase = phase,
        DirectionX = directionX,
        DirectionY = directionY,
        TargetX = 100f,
        TargetY = 200f,
        PhaseDurationTicks = 6,
    };

    private static EntityDeltaBatch Batch(int rows)
    {
        EntityDeltaBatch batch = new()
        {
            NetworkIds = new ulong[rows],
            PositionsX = new float[rows],
            PositionsY = new float[rows],
            VelocitiesX = new float[rows],
            VelocitiesY = new float[rows],
            CurrentHealths = new float[rows],
        };
        for (int i = 0; i < rows; i++)
        {
            batch.NetworkIds[i] = 100UL + (ulong)i;
            batch.PositionsX[i] = 10f + i;
            batch.PositionsY[i] = 20f + i;
            batch.VelocitiesX[i] = 0f;
            batch.VelocitiesY[i] = -3f;
            batch.CurrentHealths[i] = 80f + i;
        }

        return batch;
    }
}
