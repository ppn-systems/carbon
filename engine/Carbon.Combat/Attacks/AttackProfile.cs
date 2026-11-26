// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Combat.Attacks;

/// <summary>
/// Immutable, data-driven description of one attack: its <see cref="AttackKind"/>, usable range window,
/// phase timeline (in fixed ticks), and hit geometry. Profiles are registered once in an <see cref="AttackSystem"/>
/// and referenced by index from <see cref="AttackStateComponent.ProfileId"/>.
/// </summary>
public readonly record struct AttackProfile
{
    /// <summary>Gets the behavioral family of this attack.</summary>
    public AttackKind Kind { get; init; }

    /// <summary>Gets the minimum target distance at which this attack may be started.</summary>
    public float MinRange { get; init; }

    /// <summary>Gets the maximum target distance at which this attack may be started (leap distance cap).</summary>
    public float MaxRange { get; init; }

    /// <summary>Gets the telegraph / aim / cast / crouch duration in ticks.</summary>
    public uint WindupTicks { get; init; }

    /// <summary>Gets the lunge / detonation delay / airborne duration in ticks (0 skips the phase).</summary>
    public uint TravelTicks { get; init; }

    /// <summary>Gets the active (damage-resolving) duration in ticks. Must be at least 1.</summary>
    public uint ActiveTicks { get; init; }

    /// <summary>Gets the recovery duration in ticks (0 skips the phase).</summary>
    public uint RecoveryTicks { get; init; }

    /// <summary>Gets the cooldown in ticks, measured from the tick the attack starts.</summary>
    public uint CooldownTicks { get; init; }

    /// <summary>Gets the multiplier applied to the attacker's base damage.</summary>
    public float DamageMultiplier { get; init; }

    /// <summary>Gets the melee reach, or the AoE radius for spells and leap impacts.</summary>
    public float AreaRadius { get; init; }

    /// <summary>Gets the half-angle in radians of melee arcs and cone spells.</summary>
    public float HalfAngleRadians { get; init; }

    /// <summary>Gets the hit area geometry used by spells (leap impacts are always circles).</summary>
    public AttackAreaShape Shape { get; init; }

    /// <summary>Gets the forward distance covered during a melee lunge.</summary>
    public float LungeDistance { get; init; }

    /// <summary>Gets the projectile speed in world units per second.</summary>
    public float ProjectileSpeed { get; init; }

    /// <summary>Gets the projectile collision radius.</summary>
    public float ProjectileRadius { get; init; }

    /// <summary>Gets the purely visual apex height of the projectile arc (the simulation is planar).</summary>
    public float ArcHeight { get; init; }

    /// <summary>Gets the client projectile visual id passed to <see cref="IAttackEffects.LaunchProjectile"/>.</summary>
    public byte ProjectileVisual { get; init; }

    /// <summary>Gets the total timeline length in ticks (windup + travel + active + recovery).</summary>
    public uint TotalTicks => this.WindupTicks + this.TravelTicks + this.ActiveTicks + this.RecoveryTicks;

    /// <summary>
    /// Gets the duration in ticks of <paramref name="phase"/> for this profile.
    /// </summary>
    /// <param name="phase">Timeline phase.</param>
    /// <returns>Phase duration in ticks (0 for <see cref="AttackPhase.None"/>).</returns>
    public uint GetPhaseTicks(AttackPhase phase) => phase switch
    {
        AttackPhase.Windup => this.WindupTicks,
        AttackPhase.Travel => this.TravelTicks,
        AttackPhase.Active => this.ActiveTicks,
        AttackPhase.Recovery => this.RecoveryTicks,
        AttackPhase.None => 0U,
        _ => 0U
    };

    /// <summary>
    /// Validates that the profile is internally consistent.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when a field is out of range for the attack kind.</exception>
    public void Validate()
    {
        if (!Enum.IsDefined(this.Kind) || !Enum.IsDefined(this.Shape))
        {
            throw new ArgumentException($"Unknown attack kind '{this.Kind}' or shape '{this.Shape}'.");
        }

        if (this.MinRange < 0f || this.MaxRange <= 0f || this.MinRange > this.MaxRange)
        {
            throw new ArgumentException($"{this.Kind} attack range window [{this.MinRange}, {this.MaxRange}] is invalid.");
        }

        if (this.ActiveTicks == 0)
        {
            throw new ArgumentException($"{this.Kind} attack must have at least one active tick.");
        }

        if (this.DamageMultiplier < 0f || this.AreaRadius < 0f || this.HalfAngleRadians < 0f)
        {
            throw new ArgumentException($"{this.Kind} attack has negative damage or geometry.");
        }

        switch (this.Kind)
        {
            case AttackKind.Melee:
                if (this.AreaRadius <= 0f || this.HalfAngleRadians <= 0f)
                {
                    throw new ArgumentException("Melee attack requires a positive reach and arc half-angle.");
                }

                if (this.LungeDistance > 0f && this.TravelTicks == 0)
                {
                    throw new ArgumentException("Melee lunge distance requires lunge (travel) ticks.");
                }

                break;
            case AttackKind.Projectile:
                if (this.ProjectileSpeed <= 0f || this.ProjectileRadius <= 0f)
                {
                    throw new ArgumentException("Projectile attack requires a positive projectile speed and radius.");
                }

                break;
            case AttackKind.Spell:
                if (this.AreaRadius <= 0f || (this.Shape == AttackAreaShape.Cone && this.HalfAngleRadians <= 0f))
                {
                    throw new ArgumentException("Spell attack requires a positive area radius (and cone half-angle).");
                }

                break;
            case AttackKind.Leap:
                if (this.AreaRadius <= 0f || this.TravelTicks == 0)
                {
                    throw new ArgumentException("Leap attack requires a positive impact radius and airborne (travel) ticks.");
                }

                break;
            default:
                break;
        }
    }
}
