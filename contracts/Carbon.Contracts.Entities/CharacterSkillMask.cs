// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Contracts.Entities;

/// <summary>
/// Bitmask indicating unlocked character skills (supports up to 64 skill IDs directly in a single scalar).
/// </summary>
[Flags]
public enum CharacterSkillMask : ulong
{
    /// <summary>No active skills unlocked.</summary>
    None = 0UL,

    /// <summary>Skill ID 1 (Whirlwind) unlocked.</summary>
    Whirlwind = 1UL << 0,

    /// <summary>Skill ID 2 (Arcane Nova) unlocked.</summary>
    ArcaneNova = 1UL << 1,

    /// <summary>Skill ID 3 (Vital Surge) unlocked.</summary>
    VitalSurge = 1UL << 2,
}

/// <summary>
/// Helper utilities mapping numeric skill identifiers (<c>1..64</c>) to <see cref="CharacterSkillMask"/> bits.
/// </summary>
public static class CharacterSkillMaskHelper
{
    /// <summary>Returns the flag bit corresponding to <paramref name="skillId"/> (<c>1..64</c>).</summary>
    public static CharacterSkillMask BitForSkill(ushort skillId) =>
        skillId is >= 1 and <= 64 ? (CharacterSkillMask)(1UL << (skillId - 1)) : CharacterSkillMask.None;

    /// <summary>Returns whether <paramref name="skillId"/> is set inside <paramref name="mask"/>.</summary>
    public static bool HasSkill(CharacterSkillMask mask, ushort skillId)
    {
        CharacterSkillMask bit = BitForSkill(skillId);
        return bit != CharacterSkillMask.None && (mask & bit) == bit;
    }
}
