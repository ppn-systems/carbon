// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Combat.Statuses;

/// <summary>
/// Active buff, debuff, or periodic aura instance tracked inside a <see cref="StatusEffectBuffer"/>.
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
    bool IsDamage = true);
