// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Entities;

/// <summary>
/// Target attribute or active skill slot for player progression point allocation (<see cref="EntityOpCode.AllocateCharacterPoint"/>).
/// </summary>
public enum CharacterPointTarget : byte
{
    /// <summary>Vitality (+12 Max Health, +0.5 Defense per point).</summary>
    Vitality = 0,

    /// <summary>Wisdom (+10 Max Mana, +0.35 MP/s regeneration per point).</summary>
    Wisdom = 1,

    /// <summary>Power (+3.5% Attack Damage multiplier per point).</summary>
    Power = 2,

    /// <summary>Agility (+8 Max Stamina, +0.6% Movement Speed per point).</summary>
    Agility = 3,

    /// <summary>Unlock or rank up Skill 1: Whirlwind ([Q], max rank 5).</summary>
    SkillWhirlwind = 10,

    /// <summary>Unlock or rank up Skill 2: Arcane Nova ([R], max rank 5).</summary>
    SkillArcaneNova = 11,

    /// <summary>Unlock or rank up Skill 3: Vital Surge ([V], max rank 5).</summary>
    SkillVitalSurge = 12,
}
