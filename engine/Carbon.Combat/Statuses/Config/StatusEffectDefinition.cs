// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Combat.Statuses.Config;

/// <summary>One status effect from <c>resources/config/status_effects.json</c> (ADR-029 D1).</summary>
public sealed class StatusEffectDefinition
{
    /// <summary>The lowest movement speed multiplier any level of a slow reaches.</summary>
    public const float MinMoveSpeedMultiplier = 0.05f;

    /// <summary>The smallest view radius multiplier any level of blindness reaches (ADR-036 D9).</summary>
    public const float MinViewRadiusMultiplier = 0.25f;

    private const float MinDamageDealtMultiplier = 0.05f;

    private const float MaxMultiplier = 3f;

    private const float MaxRegenMultiplier = 10f;

    /// <summary>Gets the stable wire id (1..65535).</summary>
    public ushort Id { get; set; }

    /// <summary>Gets the localization and persistence key (<c>[a-z0-9_]</c>).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets the vector icon id (<c>resources/art/ui/&lt;icon&gt;.cvg.json</c>).</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Gets whether the effect helps or hurts.</summary>
    public StatusCategory Category { get; set; }

    /// <summary>Gets the needs condition a derived effect mirrors; <see cref="StatusDerivation.None"/> for timed effects.</summary>
    public StatusDerivation Derived { get; set; }

    /// <summary>Gets the default duration in seconds (0 for derived effects).</summary>
    public float DurationSeconds { get; set; }

    /// <summary>Gets how a repeated application merges.</summary>
    public StatusStacking Stacking { get; set; }

    /// <summary>Gets the stack cap.</summary>
    public byte MaxStacks { get; set; } = 1;

    /// <summary>Gets the seconds between damage pulses (0 = no pulses).</summary>
    public float TickIntervalSeconds { get; set; }

    /// <summary>Gets the damage per pulse per stack.</summary>
    public float Magnitude { get; set; }

    /// <summary>Gets whether damage pulses may take the last hit point.</summary>
    public bool Lethal { get; set; } = true;

    /// <summary>Gets the movement speed multiplier while active.</summary>
    public float MoveSpeedMultiplier { get; set; } = 1f;

    /// <summary>Gets whether the effect stuns (no movement, no attacks).</summary>
    public bool Stun { get; set; }

    /// <summary>Gets the out-of-combat health regeneration multiplier while active.</summary>
    public float HealthRegenMultiplier { get; set; } = 1f;

    /// <summary>Gets the stamina regeneration multiplier while active.</summary>
    public float StaminaRegenMultiplier { get; set; } = 1f;

    /// <summary>Gets whether the effect survives a relog (ADR-029 D8).</summary>
    public bool Persist { get; set; }

    /// <summary>Gets the highest level (amplifier) the effect can reach, 1..255 (ADR-036 D1); 1 means no levels.</summary>
    public byte MaxLevel { get; set; } = 1;

    /// <summary>Gets the damage per pulse per stack added by each level above I.</summary>
    public float MagnitudePerLevel { get; set; }

    /// <summary>Gets the movement speed multiplier added by each level above I (negative for a slow).</summary>
    public float MoveSpeedMultiplierPerLevel { get; set; }

    /// <summary>Gets the health regeneration multiplier added by each level above I.</summary>
    public float HealthRegenMultiplierPerLevel { get; set; }

    /// <summary>Gets the stamina regeneration multiplier added by each level above I.</summary>
    public float StaminaRegenMultiplierPerLevel { get; set; }

    /// <summary>Gets the multiplier on damage the affected player deals (weakness, ADR-036 D9).</summary>
    public float DamageDealtMultiplier { get; set; } = 1f;

    /// <summary>Gets the damage dealt multiplier added by each level above I.</summary>
    public float DamageDealtMultiplierPerLevel { get; set; }

    /// <summary>Gets the multiplier on the affected player's view (AoI) radius (blindness, ADR-036 D9).</summary>
    public float ViewRadiusMultiplier { get; set; } = 1f;

    /// <summary>Gets the view radius multiplier added by each level above I.</summary>
    public float ViewRadiusMultiplierPerLevel { get; set; }

    /// <summary>Gets whether entering water at least <c>Shallow</c> deep removes the effect (burning, ADR-036 D9).</summary>
    public bool ExtinguishedByWater { get; set; }

    /// <summary>Clamps <paramref name="level"/> to <c>[1, MaxLevel]</c>; 0 (none) becomes I.</summary>
    /// <param name="level">Requested level.</param>
    /// <returns>The level the effect is applied at.</returns>
    public byte ClampLevel(byte level) => Math.Clamp(level, (byte)1, Math.Max((byte)1, this.MaxLevel));

    /// <summary>Gets the damage per pulse per stack at <paramref name="level"/> (clamped).</summary>
    /// <param name="level">Effect level.</param>
    /// <returns>The magnitude.</returns>
    public float MagnitudeAt(byte level) =>
        MathF.Max(0f, this.Magnitude + (this.Steps(level) * this.MagnitudePerLevel));

    /// <summary>
    /// Gets the movement speed multiplier at <paramref name="level"/> (clamped), never below
    /// <see cref="MinMoveSpeedMultiplier"/>, so even the highest slow never stops a player (ADR-036 D1).
    /// </summary>
    /// <param name="level">Effect level.</param>
    /// <returns>The multiplier.</returns>
    public float MoveSpeedMultiplierAt(byte level) =>
        Math.Clamp(this.MoveSpeedMultiplier + (this.Steps(level) * this.MoveSpeedMultiplierPerLevel), MinMoveSpeedMultiplier, MaxMultiplier);

    /// <summary>Gets the health regeneration multiplier at <paramref name="level"/> (clamped to <c>[0, 10]</c>).</summary>
    /// <param name="level">Effect level.</param>
    /// <returns>The multiplier.</returns>
    public float HealthRegenMultiplierAt(byte level) =>
        Math.Clamp(this.HealthRegenMultiplier + (this.Steps(level) * this.HealthRegenMultiplierPerLevel), 0f, MaxRegenMultiplier);

    /// <summary>Gets the stamina regeneration multiplier at <paramref name="level"/> (clamped to <c>[0, 10]</c>).</summary>
    /// <param name="level">Effect level.</param>
    /// <returns>The multiplier.</returns>
    public float StaminaRegenMultiplierAt(byte level) =>
        Math.Clamp(this.StaminaRegenMultiplier + (this.Steps(level) * this.StaminaRegenMultiplierPerLevel), 0f, MaxRegenMultiplier);

    /// <summary>Gets the damage dealt multiplier at <paramref name="level"/> (clamped to <c>[0.05, 3]</c>).</summary>
    /// <param name="level">Effect level.</param>
    /// <returns>The multiplier.</returns>
    public float DamageDealtMultiplierAt(byte level) =>
        Math.Clamp(this.DamageDealtMultiplier + (this.Steps(level) * this.DamageDealtMultiplierPerLevel), MinDamageDealtMultiplier, MaxMultiplier);

    /// <summary>
    /// Gets the view radius multiplier at <paramref name="level"/>, never below <see cref="MinViewRadiusMultiplier"/>, so a
    /// blinded player still sees nearby enemies.
    /// </summary>
    /// <param name="level">Effect level.</param>
    /// <returns>The multiplier.</returns>
    public float ViewRadiusMultiplierAt(byte level) =>
        Math.Clamp(this.ViewRadiusMultiplier + (this.Steps(level) * this.ViewRadiusMultiplierPerLevel), MinViewRadiusMultiplier, MaxMultiplier);

    private int Steps(byte level) => this.ClampLevel(level) - 1;
}
