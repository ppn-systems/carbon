// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Combat.Statuses;

/// <summary>
/// Active buff, debuff, or periodic aura instance tracked inside a <see cref="StatusEffectBuffer"/>. Every value the
/// effect needs is copied in on apply, so a config reload never changes a running effect (ADR-029 D4).
/// </summary>
/// <param name="EffectId">Status effect definition identifier.</param>
/// <param name="Caster">Source entity that applied the status.</param>
/// <param name="ControlMask">Crowd-control flags contributed while active.</param>
/// <param name="MagnitudePerStack">Healing or damage magnitude per stack per pulse.</param>
/// <param name="RemainingTicks">Remaining duration in simulation ticks.</param>
/// <param name="PulseIntervalTicks">Ticks between periodic pulses (0 for non-pulsing buffs/CC).</param>
/// <param name="TicksUntilNextPulse">Countdown ticks until next periodic pulse.</param>
/// <param name="Stacks">Current stack count.</param>
/// <param name="MaxStacks">Maximum stack count permitted.</param>
/// <param name="IsDamage"><see langword="true"/> for damage-over-time; <see langword="false"/> for heal-over-time.</param>
/// <param name="Stacking">How a repeated application is merged.</param>
/// <param name="MoveSpeedMultiplier">Movement speed multiplier while active (1 = none).</param>
/// <param name="DurationTicks">Full duration of the latest application, for the client's duration ring.</param>
/// <param name="Lethal">Whether damage pulses may take the last hit point.</param>
/// <param name="HealthRegenMultiplier">Out-of-combat health regeneration multiplier while active.</param>
/// <param name="StaminaRegenMultiplier">Stamina regeneration multiplier while active.</param>
/// <param name="Source">What applied the effect.</param>
/// <param name="SourceId">Source detail (monster archetype id), 0 when none.</param>
/// <param name="Level">Effect level (amplifier), 1 = I (ADR-036). The level-scaled values are already resolved into the
/// magnitude and multipliers; the level decides how repeated applications merge and is shown to the owner.</param>
/// <param name="DamageDealtMultiplier">Multiplier on damage the owner deals while active (weakness).</param>
/// <param name="ViewRadiusMultiplier">Multiplier on the owner's view radius while active (blindness).</param>
/// <param name="ExtinguishedByWater">Whether water removes the effect (burning).</param>
public readonly record struct ActiveStatusEffect(
    uint EffectId,
    Entity Caster,
    CrowdControlMask ControlMask,
    float MagnitudePerStack,
    uint RemainingTicks,
    uint PulseIntervalTicks = 0,
    uint TicksUntilNextPulse = 0,
    byte Stacks = 1,
    byte MaxStacks = 1,
    bool IsDamage = true,
    StatusStacking Stacking = StatusStacking.Stack,
    float MoveSpeedMultiplier = 1f,
    uint DurationTicks = 0,
    bool Lethal = true,
    float HealthRegenMultiplier = 1f,
    float StaminaRegenMultiplier = 1f,
    StatusSource Source = StatusSource.None,
    ushort SourceId = 0,
    byte Level = 1,
    float DamageDealtMultiplier = 1f,
    float ViewRadiusMultiplier = 1f,
    bool ExtinguishedByWater = false)
{
    /// <summary>
    /// Gets the strength used by <see cref="StatusStacking.Strongest"/> at one level: magnitude plus the speed, damage and
    /// view reductions.
    /// </summary>
    public float Strength =>
        this.MagnitudePerStack + (1f - this.MoveSpeedMultiplier) + (1f - this.DamageDealtMultiplier) + (1f - this.ViewRadiusMultiplier);
}
