// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Statuses.Config;

/// <summary>Effect applied when one hit deals at least <see cref="MinDamageFraction"/> of the victim's max HP.</summary>
public sealed class HeavyHitRule
{
    /// <summary>Gets the effect key.</summary>
    public string Effect { get; set; } = string.Empty;

    /// <summary>Gets the minimum mitigated damage as a fraction of max HP.</summary>
    public float MinDamageFraction { get; set; } = 1f;
}

/// <summary>Buff granted when a sleeper wakes with at least <see cref="MinRestFraction"/> rest.</summary>
public sealed class RestedOnWakeRule
{
    /// <summary>Gets the effect key.</summary>
    public string Effect { get; set; } = string.Empty;

    /// <summary>Gets the minimum rest fraction at wake-up.</summary>
    public float MinRestFraction { get; set; } = 1f;
}

/// <summary>A chance for a monster type's landed hit to apply an effect (ADR-029 D7).</summary>
public sealed class MonsterStatusProc
{
    /// <summary>Gets the monster id from <c>monsters.json</c>.</summary>
    public string Monster { get; set; } = string.Empty;

    /// <summary>Gets the effect key.</summary>
    public string Effect { get; set; } = string.Empty;

    /// <summary>Gets the chance per landed hit, 0..1.</summary>
    public float Chance { get; set; }
}

/// <summary>The <c>status_effects.json</c> document.</summary>
public sealed class StatusEffectsDocument
{
    /// <summary>Gets the schema version (1).</summary>
    public int SchemaVersion { get; set; }

    /// <summary>Gets the heavy-hit rule, or <see langword="null"/> for none.</summary>
    public HeavyHitRule? HeavyHit { get; set; }

    /// <summary>Gets the wake-up buff rule, or <see langword="null"/> for none.</summary>
    public RestedOnWakeRule? RestedOnWake { get; set; }

    /// <summary>Gets the effect definitions.</summary>
    public StatusEffectDefinition[] Effects { get; set; } = [];

    /// <summary>Gets the monster procs.</summary>
    public MonsterStatusProc[] MonsterProcs { get; set; } = [];
}
