// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Combat.Attacks;

namespace Carbon.Creatures.Definitions;

/// <summary>
/// JSON model of one monster attack; compiled into an unmanaged <see cref="AttackProfile"/> by <see cref="MonsterCatalog"/>.
/// Attacks are listed in priority order: the first ready attack whose range window contains the target is used.
/// </summary>
public sealed class AttackProfileDefinition
{
    /// <summary>Gets the attack kind.</summary>
    public AttackKind Kind { get; set; }

    /// <summary>Gets the minimum usable target distance.</summary>
    public float MinRange { get; set; }

    /// <summary>Gets the maximum usable target distance.</summary>
    public float MaxRange { get; set; }

    /// <summary>Gets the windup (telegraph / aim / cast / crouch) ticks.</summary>
    public uint WindupTicks { get; set; }

    /// <summary>Gets the travel (lunge / detonation delay / airborne) ticks.</summary>
    public uint TravelTicks { get; set; }

    /// <summary>Gets the active (hit frames / release / detonation / landing) ticks.</summary>
    public uint ActiveTicks { get; set; } = 1;

    /// <summary>Gets the recovery ticks.</summary>
    public uint RecoveryTicks { get; set; }

    /// <summary>Gets the cooldown ticks measured from attack start.</summary>
    public uint CooldownTicks { get; set; }

    /// <summary>Gets the multiplier applied to the monster's scaled damage.</summary>
    public float DamageMultiplier { get; set; } = 1f;

    /// <summary>Gets the melee reach or AoE radius.</summary>
    public float AreaRadius { get; set; }

    /// <summary>Gets the melee arc / cone half-angle in degrees.</summary>
    public float HalfAngleDegrees { get; set; }

    /// <summary>Gets the spell AoE shape.</summary>
    public AttackAreaShape Shape { get; set; }

    /// <summary>Gets the melee lunge distance.</summary>
    public float LungeDistance { get; set; }

    /// <summary>Gets the projectile speed.</summary>
    public float ProjectileSpeed { get; set; }

    /// <summary>Gets the projectile collision radius.</summary>
    public float ProjectileRadius { get; set; }

    /// <summary>Gets the visual projectile arc height.</summary>
    public float ArcHeight { get; set; }

    /// <summary>
    /// Compiles this definition into a validated simulation <see cref="AttackProfile"/>.
    /// </summary>
    /// <returns>The compiled profile.</returns>
    public AttackProfile ToProfile()
    {
        AttackProfile profile = new()
        {
            Kind = this.Kind,
            MinRange = this.MinRange,
            MaxRange = this.MaxRange,
            WindupTicks = this.WindupTicks,
            TravelTicks = this.TravelTicks,
            ActiveTicks = this.ActiveTicks,
            RecoveryTicks = this.RecoveryTicks,
            CooldownTicks = this.CooldownTicks,
            DamageMultiplier = this.DamageMultiplier,
            AreaRadius = this.AreaRadius,
            HalfAngleRadians = this.HalfAngleDegrees * (MathF.PI / 180f),
            Shape = this.Shape,
            LungeDistance = this.LungeDistance,
            ProjectileSpeed = this.ProjectileSpeed,
            ProjectileRadius = this.ProjectileRadius,
            ArcHeight = this.ArcHeight
        };
        profile.Validate();
        return profile;
    }
}
