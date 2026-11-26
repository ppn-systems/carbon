// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Attacks;

/// <summary>
/// Hit area geometry for area-resolving attacks (spells and leap impacts) and their client ground telegraphs.
/// </summary>
public enum AttackAreaShape : byte
{
    /// <summary>Circle of <see cref="AttackProfile.AreaRadius"/> around the locked target point (spell) or landing point (leap).</summary>
    Circle = 0,

    /// <summary>Cone from the attacker along the locked direction with <see cref="AttackProfile.HalfAngleRadians"/>.</summary>
    Cone = 1
}
