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

    /// <summary>Returns MP cost, Stamina cost, cooldown seconds, and base name for <paramref name="skillSlot"/> (<c>1..3</c>).</summary>
    public static bool TryGetSkillSpec(
        byte skillSlot,
        out string displayName,
        out float manaCost,
        out float staminaCost,
        out float cooldownSeconds)
    {
        switch (skillSlot)
        {
            case SkillWhirlwind:
                displayName = "Whirlwind [Q]";
                manaCost = 18f;
                staminaCost = 22f;
                cooldownSeconds = 3.5f;
                return true;

            case SkillArcaneNova:
                displayName = "Arcane Nova [R]";
                manaCost = 30f;
                staminaCost = 10f;
                cooldownSeconds = 5.0f;
                return true;

            case SkillVitalSurge:
                displayName = "Vital Surge [V]";
                manaCost = 35f;
                staminaCost = 0f;
                cooldownSeconds = 8.0f;
                return true;

            default:
                displayName = string.Empty;
                manaCost = 0f;
                staminaCost = 0f;
                cooldownSeconds = 0f;
                return false;
        }
    }
}
