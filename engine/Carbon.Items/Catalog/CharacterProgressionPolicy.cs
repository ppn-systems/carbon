// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Progression;

namespace Carbon.Items.Catalog;

/// <summary>
/// Computed player combat and resource attributes combining character level progression with equipped gear.
/// </summary>
/// <param name="MaxHealth">Max health.</param>
/// <param name="MaxMana">Max mana.</param>
/// <param name="MaxStamina">Max stamina.</param>
/// <param name="Defense">Physical defense rating.</param>
/// <param name="AttackMultiplier">Physical attack multiplier.</param>
/// <param name="MoveSpeedMultiplier">Movement speed multiplier (capped).</param>
/// <param name="SpellPowerMultiplier">Magic attack multiplier.</param>
/// <param name="MagicResist">Magic resist rating.</param>
/// <param name="CritChance">Critical chance including the held weapon (capped).</param>
/// <param name="CritDamage">Critical damage multiplier (capped).</param>
/// <param name="AttackSpeedMultiplier">Attack speed multiplier (capped); divides the attack cooldown.</param>
/// <param name="Evasion">Evasion rating; the dodge chance depends on the attacker's level.</param>
/// <param name="HealthRegenPerSecond">Out-of-combat health regeneration per second.</param>
/// <param name="ManaRegenPerSecond">Mana regeneration per second.</param>
public readonly record struct DerivedCharacterStats(
    float MaxHealth,
    float MaxMana,
    float MaxStamina,
    float Defense,
    float AttackMultiplier,
    float MoveSpeedMultiplier,
    float SpellPowerMultiplier = 1f,
    float MagicResist = 0f,
    float CritChance = 0f,
    float CritDamage = 1.5f,
    float AttackSpeedMultiplier = 1f,
    float Evasion = 0f,
    float HealthRegenPerSecond = 3f,
    float ManaRegenPerSecond = 6f);

/// <summary>
/// Rank-evaluated combat/resource specification for an active player skill.
/// </summary>
public readonly record struct SkillSpec(
    ushort SkillId,
    string DisplayName,
    float ManaCost,
    float StaminaCost,
    float CooldownSeconds,
    float Radius,
    float HalfAngleRad,
    float DamageMultiplier,
    float SelfHealRatio,
    float StaminaRestore);

/// <summary>
/// Skill point rewards and active skill evaluation via <see cref="SkillCatalog"/>; derived stats are computed by
/// <see cref="CharacterStatCalculator"/>. Experience, attribute points, stat growth and mitigation come from <c>progression.json</c>
/// (ADR-021); this class holds no balance table for them.
/// </summary>
public static class CharacterProgressionPolicy
{
    /// <summary>
    /// Level clamp for the legacy <see cref="WorldActorCatalog"/> tables. The character level cap comes from
    /// <c>progression.json</c> (<see cref="ProgressionConfig.MaxLevel"/>).
    /// </summary>
    public const ushort MaxLevel = 99;

    /// <summary>Maximum mastery rank per active skill.</summary>
    public const byte MaxSkillRank = 5;

    /// <summary>
    /// Returns cumulative skill mastery points earned by <paramref name="level"/> (<c>1 SP</c> at Lv.1,
    /// <c>+1</c> per level, capped at total spendable skill ranks across <see cref="SkillCatalog"/>).
    /// </summary>
    public static ushort GetTotalSkillPointsForLevel(ushort level) =>
        (ushort)Math.Min(SkillCatalog.TotalLearnableRanks, Math.Max(1, (int)level));

    /// <summary>Computes unspent skill points remaining for <paramref name="level"/> after <paramref name="state"/>.</summary>
    public static ushort GetUnspentSkillPoints(ushort level, CharacterProgressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int total = GetTotalSkillPointsForLevel(level);
        return (ushort)Math.Max(0, total - state.GetTotalSpentSkillPoints());
    }

    /// <summary>Resolves baseline Rank 1 specs for <paramref name="skillId"/> via <see cref="SkillCatalog"/>.</summary>
    public static bool TryGetSkillSpec(ushort skillId, out SkillSpec spec) =>
        TryGetSkillSpec(skillId, rank: 1, out spec);

    /// <summary>Resolves rank-scaled combat &amp; resource specs for <paramref name="skillId"/> via <see cref="SkillCatalog"/>.</summary>
    public static bool TryGetSkillSpec(ushort skillId, byte rank, out SkillSpec spec)
    {
        if (!SkillCatalog.Skills.TryGet(skillId, out SkillDefinition? def))
        {
            spec = default;
            return false;
        }

        spec = def.EvaluateRank(rank);
        return true;
    }

    /// <summary>Returns MP cost, Stamina cost, cooldown seconds, and base name for <paramref name="skillId"/>.</summary>
    public static bool TryGetSkillSpec(
        ushort skillId,
        out string displayName,
        out float manaCost,
        out float staminaCost,
        out float cooldownSeconds)
    {
        if (TryGetSkillSpec(skillId, rank: 1, out SkillSpec spec))
        {
            displayName = spec.DisplayName;
            manaCost = spec.ManaCost;
            staminaCost = spec.StaminaCost;
            cooldownSeconds = spec.CooldownSeconds;
            return true;
        }

        displayName = string.Empty;
        manaCost = 0f;
        staminaCost = 0f;
        cooldownSeconds = 0f;
        return false;
    }
}
