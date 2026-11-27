// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.IO;

namespace Carbon.Items.Progression;

/// <summary>
/// One set of character stat contributions from <c>progression.json</c>: the level-1 base, the per-level growth, or
/// the effect of one point in an attribute. Every field adds to the matching derived stat; omitted fields are 0.
/// </summary>
public sealed class StatBlock
{
    /// <summary>Gets the maximum health.</summary>
    public float MaxHealth { get; init; }

    /// <summary>Gets the maximum mana.</summary>
    public float MaxMana { get; init; }

    /// <summary>Gets the maximum stamina.</summary>
    public float MaxStamina { get; init; }

    /// <summary>Gets the physical defense rating (mitigates physical hits, see <see cref="MitigationCurve"/>).</summary>
    public float Defense { get; init; }

    /// <summary>Gets the magic resist rating (mitigates magical hits).</summary>
    public float MagicResist { get; init; }

    /// <summary>Gets the physical attack multiplier (1 = +0%).</summary>
    public float Attack { get; init; }

    /// <summary>Gets the magic attack (spell power) multiplier.</summary>
    public float SpellPower { get; init; }

    /// <summary>Gets the critical hit chance (0..1), added to the held weapon's chance.</summary>
    public float CritChance { get; init; }

    /// <summary>Gets the critical damage multiplier (1.5 = crits deal 150%).</summary>
    public float CritDamage { get; init; }

    /// <summary>Gets the attack speed multiplier (divides the attack cooldown).</summary>
    public float AttackSpeed { get; init; }

    /// <summary>Gets the movement speed multiplier.</summary>
    public float MoveSpeed { get; init; }

    /// <summary>Gets the evasion rating (converted to a dodge chance against each attacker's level).</summary>
    public float Evasion { get; init; }

    /// <summary>Gets the out-of-combat health regeneration per second.</summary>
    public float HealthRegen { get; init; }

    /// <summary>Gets the mana regeneration per second.</summary>
    public float ManaRegen { get; init; }

    /// <summary>Gets a value indicating whether every field is zero or positive.</summary>
    public bool IsNonNegative =>
        this.MaxHealth >= 0f && this.MaxMana >= 0f && this.MaxStamina >= 0f && this.Defense >= 0f &&
        this.MagicResist >= 0f && this.Attack >= 0f && this.SpellPower >= 0f && this.CritChance >= 0f &&
        this.CritDamage >= 0f && this.AttackSpeed >= 0f && this.MoveSpeed >= 0f && this.Evasion >= 0f &&
        this.HealthRegen >= 0f && this.ManaRegen >= 0f;

    /// <summary>Checks that every contribution is finite, non-negative and below a sanity bound.</summary>
    /// <param name="name">Section name for error messages.</param>
    /// <exception cref="InvalidDataException">Thrown when a value is negative, not finite or absurdly large.</exception>
    public void ValidateGrowth(string name)
    {
        if (!this.IsNonNegative)
        {
            throw new InvalidDataException($"progression.json '{name}' must not contain negative values (stats must never shrink with level or points).");
        }

        CheckBound(this.MaxHealth, 10_000f, name, "maxHealth");
        CheckBound(this.MaxMana, 10_000f, name, "maxMana");
        CheckBound(this.MaxStamina, 10_000f, name, "maxStamina");
        CheckBound(this.Defense, 10_000f, name, "defense");
        CheckBound(this.MagicResist, 10_000f, name, "magicResist");
        CheckBound(this.Attack, 10f, name, "attack");
        CheckBound(this.SpellPower, 10f, name, "spellPower");
        CheckBound(this.CritChance, 1f, name, "critChance");
        CheckBound(this.CritDamage, 10f, name, "critDamage");
        CheckBound(this.AttackSpeed, 10f, name, "attackSpeed");
        CheckBound(this.MoveSpeed, 10f, name, "moveSpeed");
        CheckBound(this.Evasion, 10_000f, name, "evasion");
        CheckBound(this.HealthRegen, 10_000f, name, "healthRegen");
        CheckBound(this.ManaRegen, 10_000f, name, "manaRegen");
    }

    private static void CheckBound(float value, float max, string section, string field)
    {
        if (!float.IsFinite(value) || value > max)
        {
            throw new InvalidDataException($"progression.json '{section}.{field}' = {value} must be finite and at most {max}.");
        }
    }
}
