// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Creatures.Scaling;

/// <summary>
/// The abyss boss and its lairs (ADR-032 D15), <c>spawning.json → bossLairs</c>. Durations are in simulation ticks and
/// distances in world units (32 per tile).
/// </summary>
public sealed class BossLairConfig
{
    /// <summary>Gets the boss monster id (a <c>monsters.json</c> entry with <c>isBoss</c>).</summary>
    public string Monster { get; set; } = string.Empty;

    /// <summary>Gets the chance, 0..1, that a lair cell with open abyss holds a lair.</summary>
    public float LairChance { get; set; }

    /// <summary>Gets how close to its lair a player must come for the boss to rise.</summary>
    public float ActivationRadius { get; set; }

    /// <summary>Gets the ticks between the boss's death and its next rise.</summary>
    public uint RespawnTicks { get; set; }

    /// <summary>Gets the ticks with no player within <see cref="ActivationRadius"/> after which the boss sinks back (full health).</summary>
    public uint IdleDespawnTicks { get; set; }

    /// <summary>Gets the ticks a fight keeps the boss from sinking back, counted from its last blow or wound.</summary>
    public uint CombatGraceTicks { get; set; }
}
