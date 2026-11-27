// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Statuses.Config;

/// <summary>One status effect from <c>resources/config/status_effects.json</c> (ADR-029 D1).</summary>
public sealed class StatusEffectDefinition
{
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
}
