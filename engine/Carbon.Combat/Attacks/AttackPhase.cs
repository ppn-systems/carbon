// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Attacks;

/// <summary>
/// Generic attack timeline phase. The meaning of each phase depends on <see cref="AttackKind"/>:
/// <list type="table">
/// <item><term><see cref="AttackKind.Melee"/></term><description>Windup = telegraph, Travel = lunge, Active = hit frames.</description></item>
/// <item><term><see cref="AttackKind.Projectile"/></term><description>Windup = aim, Active = release (projectile spawned on first tick).</description></item>
/// <item><term><see cref="AttackKind.Spell"/></term><description>Windup = cast channel, Travel = delayed detonation, Active = detonation.</description></item>
/// <item><term><see cref="AttackKind.Leap"/></term><description>Windup = crouch, Travel = airborne, Active = landing impact.</description></item>
/// </list>
/// Phases with a zero duration are skipped and never reported.
/// </summary>
public enum AttackPhase : byte
{
    /// <summary>No attack in progress (also published when an attack finishes or is interrupted).</summary>
    None = 0,

    /// <summary>Telegraph / aim / cast channel / crouch.</summary>
    Windup = 1,

    /// <summary>Lunge / delayed detonation / airborne.</summary>
    Travel = 2,

    /// <summary>Hit frames / release / detonation / landing impact. The only phase that resolves damage.</summary>
    Active = 3,

    /// <summary>Post-attack recovery; the attacker is vulnerable and rooted.</summary>
    Recovery = 4
}
