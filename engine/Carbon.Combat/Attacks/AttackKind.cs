// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Attacks;

/// <summary>
/// Behavioral family of a data-driven <see cref="AttackProfile"/>. Each kind runs a distinct phase timeline in
/// <see cref="AttackSystem"/> and maps to a distinct client animation set.
/// </summary>
public enum AttackKind : byte
{
    /// <summary>Close-range swing: wind-up telegraph, lunge, active hit frames (arc), recovery.</summary>
    Melee = 0,

    /// <summary>Aimed shot: aim (tracks target), release (spawns a ballistic projectile), recovery.</summary>
    Projectile = 1,

    /// <summary>Spell: cast channel with locked ground telegraph, delayed detonation (circle or cone AoE), recovery.</summary>
    Spell = 2,

    /// <summary>Charge / leap: crouch, airborne travel to a locked landing point, landing impact AoE, recovery.</summary>
    Leap = 3
}
