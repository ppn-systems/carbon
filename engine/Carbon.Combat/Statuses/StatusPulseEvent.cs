// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Combat.Statuses;

/// <summary>
/// Emitted when a periodic status effect (DoT or HoT) pulses on a fixed simulation tick.
/// </summary>
/// <param name="Caster">Entity that applied the status effect.</param>
/// <param name="Target">Entity receiving the periodic pulse.</param>
/// <param name="EffectId">Status effect definition identifier.</param>
/// <param name="TotalMagnitude">Total pulse value (<c>MagnitudePerStack * Stacks</c>).</param>
/// <param name="IsDamage"><see langword="true"/> if damage; <see langword="false"/> if healing.</param>
/// <param name="Lethal">Whether a damage pulse may take the last hit point.</param>
/// <param name="Applied">Health actually removed or restored, set by <see cref="StatusEffectSystem"/> when it publishes.</param>
public readonly record struct StatusPulseEvent(
    Entity Caster,
    Entity Target,
    uint EffectId,
    float TotalMagnitude,
    bool IsDamage,
    bool Lethal = true,
    float Applied = 0f);
