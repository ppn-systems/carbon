// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;

namespace Carbon.Items.Catalog;

/// <summary>
/// Computed player combat and resource attributes combining character level progression with equipped armor.
/// </summary>
public readonly record struct DerivedCharacterStats(
    float MaxHealth,
    float MaxMana,
    float MaxStamina,
    float Defense,
    float AttackMultiplier,
    float MoveSpeedMultiplier);

/// <summary>
/// Declarative combat/resource specification for an active player skill.
/// </summary>
public readonly record struct SkillSpec(
    byte SkillSlot,
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
/// Central progression policy governing character leveling curves, armor stat aggregation, damage mitigation,
/// stamina/mana economy, and active skill costs.
/// </summary>
public static class CharacterProgressionPolicy
{
    /// <summary>360-degree spinning cleave around the player.</summary>
    public const byte SkillWhirlwind = 1;

    /// <summary>Long-range crystal shockwave cone in aim direction.</summary>
    public const byte SkillArcaneNova = 2;

    /// <summary>Self restoration + stamina surge burst.</summary>
    public const byte SkillVitalSurge = 3;

    /// <summary>Maximum character progression level.</summary>
    public const ushort MaxLevel = 99;

    /// <summary>Returns total XP required to advance from <paramref name="level"/> to <c>level + 1</c>.</summary>
    public static uint GetXpToNextLevel(ushort level)
    {
        uint lv = Math.Max(1U, level);
        return 100U + ((lv - 1U) * 75U);
    }

    /// <summary>Computes derived HP, MP, Stamina, Defense, and Offense scaling for <paramref name="level"/> and armor slots.</summary>
    public static DerivedCharacterStats ComputeStats(ushort level, InventoryContainer inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        ushort lv = Math.Max((ushort)1, level);
        float maxHp = 100f + ((lv - 1) * 14f);
        float maxMp = 80f + ((lv - 1) * 10f);
        float maxStam = 100f + ((lv - 1) * 6f);
        float defTotal = 2f + ((lv - 1) * 0.5f);
        float atkMult = 1f + ((lv - 1) * 0.04f);

        for (int slotIdx = PlayerInventoryLayout.HeadSlot; slotIdx <= PlayerInventoryLayout.FeetSlot; slotIdx++)
        {
            if (slotIdx >= inventory.Capacity)
            {
                break;
            }

            ItemSlot armor = inventory.GetSlot(slotIdx);
            if (armor.IsEmpty || ToolUsagePolicy.IsWornOut(in armor))
            {
                continue;
            }

            float upgMult = ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in armor));
            maxHp += StarterGameCatalog.GetArmorBonusHealth(armor.ItemId) * upgMult;
            maxMp += StarterGameCatalog.GetArmorBonusMana(armor.ItemId) * upgMult;
            defTotal += StarterGameCatalog.GetArmorDefense(armor.ItemId) * upgMult;
        }

        return new DerivedCharacterStats(maxHp, maxMp, maxStam, defTotal, atkMult, 1f);
    }

    /// <summary>Mitigates incoming raw damage using <paramref name="defense"/> with diminishing hyperbolic scaling.</summary>
    public static float MitigateDamage(float rawDamage, float defense)
    {
        if (rawDamage <= 0f)
        {
            return 0f;
        }

        float effDef = MathF.Max(0f, defense);
        return MathF.Max(1f, rawDamage * (100f / (100f + (effDef * 4f))));
    }

    /// <summary>Resolves full combat &amp; resource specs for <paramref name="skillSlot"/> (<c>1..3</c>).</summary>
    public static bool TryGetSkillSpec(byte skillSlot, out SkillSpec spec)
    {
        spec = skillSlot switch
        {
            SkillWhirlwind => new SkillSpec(
                SkillWhirlwind,
                "Whirlwind [Q]",
                ManaCost: 18f,
                StaminaCost: 22f,
                CooldownSeconds: 3.5f,
                Radius: 96f,
                HalfAngleRad: MathF.PI,
                DamageMultiplier: 1.55f,
                SelfHealRatio: 0f,
                StaminaRestore: 0f),
            SkillArcaneNova => new SkillSpec(
                SkillArcaneNova,
                "Arcane Nova [R]",
                ManaCost: 30f,
                StaminaCost: 10f,
                CooldownSeconds: 5.0f,
                Radius: 170f,
                HalfAngleRad: 0.72f,
                DamageMultiplier: 2.10f,
                SelfHealRatio: 0f,
                StaminaRestore: 0f),
            SkillVitalSurge => new SkillSpec(
                SkillVitalSurge,
                "Vital Surge [V]",
                ManaCost: 35f,
                StaminaCost: 0f,
                CooldownSeconds: 8.0f,
                Radius: 0f,
                HalfAngleRad: 0f,
                DamageMultiplier: 0f,
                SelfHealRatio: 0.40f,
                StaminaRestore: 45f),
            _ => default,
        };
        return spec.SkillSlot != 0;
    }

    /// <summary>Returns MP cost, Stamina cost, cooldown seconds, and base name for <paramref name="skillSlot"/> (<c>1..3</c>).</summary>
    public static bool TryGetSkillSpec(
        byte skillSlot,
        out string displayName,
        out float manaCost,
        out float staminaCost,
        out float cooldownSeconds)
    {
        if (TryGetSkillSpec(skillSlot, out SkillSpec spec))
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
