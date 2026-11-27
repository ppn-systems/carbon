// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.StatusEffects;

/// <summary>
/// One active status effect of the local player as last synced by the server (<c>StatusEffectSync</c>, ADR-029 D9), with
/// the remaining time counted down locally.
/// </summary>
/// <param name="EffectId">Effect id from <c>status_effects.json</c>.</param>
/// <param name="Stacks">Stacks (severity for derived effects).</param>
/// <param name="RemainingSeconds">Seconds left; 0 for a derived effect that lasts while its condition holds.</param>
/// <param name="DurationSeconds">Full duration of the latest application; 0 for derived effects.</param>
/// <param name="Magnitude">Damage per pulse (all stacks) for damage effects, otherwise the effect's multiplier.</param>
/// <param name="SourceKind">What applied it (<c>StatusSource</c> byte).</param>
/// <param name="SourceId">Monster archetype id, or 0.</param>
public readonly record struct LocalStatusEffect(
    ushort EffectId,
    byte Stacks,
    float RemainingSeconds,
    float DurationSeconds,
    float Magnitude,
    byte SourceKind,
    ushort SourceId)
{
    /// <summary>Gets a value indicating whether the effect has a timer (derived effects do not).</summary>
    public bool IsTimed => this.DurationSeconds > 0f;

    /// <summary>Gets the remaining share of the duration, 0..1 (1 for untimed effects).</summary>
    public float RemainingFraction => this.IsTimed ? System.Math.Clamp(this.RemainingSeconds / this.DurationSeconds, 0f, 1f) : 1f;
}
