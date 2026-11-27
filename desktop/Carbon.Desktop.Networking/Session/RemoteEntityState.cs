// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.S2C;
using Carbon.Desktop.Abstractions.World;
using Carbon.Prediction.Remote;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Client-side state of one replicated entity (another player, a monster, an NPC or ground loot): what it looks like,
/// where it is, and the attack it is playing. It applies the server's broadcasts for itself and sanitises their input;
/// the session routes packets to it and owns the collection (audit round 2, SRP-1 / OOP).
/// </summary>
internal sealed class RemoteEntityState
{
    public ulong NetworkId { get; init; }
    public EntityKind Kind { get; init; }
    public ushort ArchetypeId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string? NameKey { get; init; }
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }
    public Vector2 FacingDir { get; set; } = new(0f, 1f);
    public float AttackAnimTimer { get; set; }
    public float AttackAnimDuration { get; set; } = 0.30f;
    public float HitFlashTimer { get; set; }
    public float Radius { get; init; }
    public float CurrentHealth { get; set; }
    public float MaxHealth { get; set; }
    public ushort EquippedHeadId { get; set; }
    public ushort EquippedChestId { get; set; }
    public ushort EquippedLegsId { get; set; }
    public ushort EquippedFeetId { get; set; }
    public ushort EquippedWeaponId { get; set; }
    public ushort Level { get; set; } = 1;
    public int LootCount { get; init; }
    public byte WeaponUpgradeLevel { get; set; }
    public LootCurrency LootCurrency { get; init; }
    public ulong LootOwnerNetworkId { get; init; }
    public float LootProtectionRemaining { get; set; }
    public ActionAnimationKind ActiveActionKind { get; set; }
    public byte AttackKind { get; set; }
    public byte AttackPhase { get; set; }
    public float AttackPhaseElapsed { get; set; }
    public float AttackPhaseDuration { get; set; }
    public Vector2 AttackTarget { get; set; }
    public Vector2 AttackDirection { get; set; }
    public float AttackAreaRadius { get; set; }
    public float AttackHalfAngle { get; set; }
    public byte AttackShape { get; set; }
    public byte ComboStep { get; set; }
    public SnapshotInterpolationBuffer Interpolation { get; } = new(16);

    private AttackComboCounter _combo;

    /// <summary>Starts a swing animation of <paramref name="kind"/> lasting <paramref name="duration"/> seconds.</summary>
    /// <param name="kind">Action kind.</param>
    /// <param name="duration">Clip seconds; a hero attack style uses its <see cref="HeroAttackTiming"/> instead.</param>
    /// <param name="nowSeconds">Monotonic client time, for the combo step.</param>
    public void StartSwing(ActionAnimationKind kind, float duration, float nowSeconds)
    {
        this.ComboStep = _combo.Next(kind, nowSeconds);
        HeroAttackTiming timing = HeroAttackTiming.For(HeroAttackTiming.StyleOf(kind, this.ComboStep));
        this.ActiveActionKind = kind;
        this.AttackAnimDuration = timing.Total > 0f ? timing.Total : duration;
        this.AttackAnimTimer = this.AttackAnimDuration;
    }

    public static RemoteEntityState FromSpawn(EntitySpawn pkt, string? nameKey) => new()
    {
        NetworkId = pkt.NetworkId,
        Kind = pkt.Kind,
        ArchetypeId = pkt.ArchetypeId,
        Name = pkt.Name,
        NameKey = nameKey,
        Position = new Vector2(pkt.PositionX, pkt.PositionY),
        Velocity = new Vector2(pkt.VelocityX, pkt.VelocityY),
        Radius = pkt.Radius > 0f ? pkt.Radius : 7f,
        CurrentHealth = pkt.CurrentHealth,
        MaxHealth = pkt.MaxHealth,
        EquippedHeadId = pkt.EquippedHeadId,
        EquippedChestId = pkt.EquippedChestId,
        EquippedLegsId = pkt.EquippedLegsId,
        EquippedFeetId = pkt.EquippedFeetId,
        EquippedWeaponId = pkt.EquippedWeaponId,
        Level = Math.Max((ushort)1, pkt.Level),
        LootCount = pkt.LootCount,
        WeaponUpgradeLevel = pkt.WeaponUpgradeLevel,
        LootCurrency = pkt.LootCurrency,
        LootOwnerNetworkId = pkt.LootOwnerNetworkId,
        LootProtectionRemaining = pkt.LootProtectionMs / 1000f,
    };

    /// <summary>Applies an equipment broadcast: visible gear, weapon upgrade, level (at least 1) and a positive max health.</summary>
    /// <param name="pkt">The broadcast for this entity.</param>
    public void ApplyEquipment(EntityEquipmentChanged pkt)
    {
        this.EquippedHeadId = pkt.EquippedHeadId;
        this.EquippedChestId = pkt.EquippedChestId;
        this.EquippedLegsId = pkt.EquippedLegsId;
        this.EquippedFeetId = pkt.EquippedFeetId;
        this.EquippedWeaponId = pkt.EquippedWeaponId;
        this.WeaponUpgradeLevel = pkt.WeaponUpgradeLevel;
        this.Level = Math.Max((ushort)1, pkt.Level);
        if (pkt.MaxHealth > 0f)
        {
            this.MaxHealth = MathF.Max(1f, pkt.MaxHealth);
        }
    }

    /// <summary>
    /// Starts an attack phase from the server: faces a finite, non-zero direction, clamps the phase, area and arc, and falls
    /// back to the entity's position and facing for non-finite input.
    /// </summary>
    /// <param name="pkt">The attack phase for this entity.</param>
    /// <param name="tickRate">Server ticks per second (0 is treated as 1).</param>
    public void ApplyAttackPhase(EntityAttackPhase pkt, ushort tickRate)
    {
        Vector2 dir = new(pkt.DirectionX, pkt.DirectionY);
        bool finiteDir = float.IsFinite(dir.X) && float.IsFinite(dir.Y) && dir.LengthSquared() > 1e-4f;
        if (finiteDir && pkt.Phase != EntityAttackPhase.PhaseNone)
        {
            this.FacingDir = Vector2.Normalize(dir);
        }

        this.AttackKind = pkt.AttackKind;
        this.AttackPhase = pkt.Phase > EntityAttackPhase.PhaseRecovery ? EntityAttackPhase.PhaseNone : pkt.Phase;
        this.AttackPhaseDuration = pkt.PhaseDurationTicks / (float)Math.Max((ushort)1, tickRate);

        // A phase that was already under way when it was sent starts the local clock there, so the telegraph lines up
        // with the server's (ADR-013 D2).
        this.AttackPhaseElapsed = float.IsFinite(pkt.Progress) ? Math.Clamp(pkt.Progress, 0f, 1f) * this.AttackPhaseDuration : 0f;
        this.AttackTarget = float.IsFinite(pkt.TargetX) && float.IsFinite(pkt.TargetY)
            ? new Vector2(pkt.TargetX, pkt.TargetY)
            : this.Position;
        this.AttackDirection = finiteDir ? Vector2.Normalize(dir) : this.FacingDir;
        this.AttackAreaRadius = float.IsFinite(pkt.AreaRadius) ? Math.Clamp(pkt.AreaRadius, 0f, 512f) : 0f;
        this.AttackHalfAngle = float.IsFinite(pkt.HalfAngleRadians) ? Math.Clamp(pkt.HalfAngleRadians, 0f, MathF.PI) : 0f;
        this.AttackShape = pkt.Shape;
    }

    /// <summary>
    /// Applies this entity's row of a delta batch (columns shorter than <paramref name="index"/> are skipped), turns to face
    /// a fast enough velocity, and records an interpolation snapshot at <paramref name="snapshotTime"/>.
    /// </summary>
    /// <param name="pkt">The delta batch.</param>
    /// <param name="index">This entity's row in the batch columns.</param>
    /// <param name="snapshotTime">Server time of the batch, in seconds.</param>
    public void ApplyDelta(EntityDeltaBatch pkt, int index, double snapshotTime)
    {
        if (index < pkt.PositionsX.Length && index < pkt.PositionsY.Length)
        {
            this.Position = new Vector2(pkt.PositionsX[index], pkt.PositionsY[index]);
        }

        if (index < pkt.VelocitiesX.Length && index < pkt.VelocitiesY.Length)
        {
            this.Velocity = new Vector2(pkt.VelocitiesX[index], pkt.VelocitiesY[index]);
            if (this.Velocity.LengthSquared() > 4f)
            {
                this.FacingDir = Vector2.Normalize(this.Velocity);
            }
        }

        if (index < pkt.CurrentHealths.Length)
        {
            this.CurrentHealth = pkt.CurrentHealths[index];
        }

        _ = this.Interpolation.PushSnapshot(new TransformSnapshot(snapshotTime, this.Position, this.Velocity));
    }

    public AttackPhaseView BuildAttackView() => new(
        this.AttackKind,
        this.AttackPhase,
        this.AttackPhaseDuration > 0f ? Math.Clamp(this.AttackPhaseElapsed / this.AttackPhaseDuration, 0f, 1f) : 1f,
        this.AttackTarget,
        this.AttackDirection,
        this.AttackAreaRadius,
        this.AttackHalfAngle,
        this.AttackShape);
}