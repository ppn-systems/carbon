// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Entities;
using Carbon.Items.Containers;

namespace Carbon.Items.Catalog;

/// <summary>
/// Computed player combat and resource attributes combining character level progression with equipped gear.
/// </summary>
public readonly record struct DerivedCharacterStats(
    float MaxHealth,
    float MaxMana,
    float MaxStamina,
    float Defense,
    float AttackMultiplier,
    float MoveSpeedMultiplier,
    float SpellPowerMultiplier = 1f);

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
/// Central progression policy governing character leveling curves, attribute &amp; skill point rewards,
/// armor stat aggregation, damage mitigation, stamina/mana economy, and active skill evaluation via <see cref="SkillCatalog"/>.
/// </summary>
public static class CharacterProgressionPolicy
{
    /// <summary>Maximum character progression level.</summary>
    public const ushort MaxLevel = 99;

    /// <summary>Maximum mastery rank per active skill.</summary>
    public const byte MaxSkillRank = 5;

    /// <summary>Attribute points granted upon each level up above Level 1.</summary>
    public const int StatPointsPerLevel = 4;

    /// <summary>Returns cumulative attribute points earned by <paramref name="level"/>.</summary>
    public static ushort GetTotalStatPointsForLevel(ushort level) =>
        (ushort)(Math.Max(0, level - 1) * StatPointsPerLevel);

    /// <summary>
    /// Returns cumulative skill mastery points earned by <paramref name="level"/> (<c>1 SP</c> at Lv.1,
    /// <c>+1</c> per level, capped at total spendable skill ranks across <see cref="SkillCatalog"/>).
    /// </summary>
    public static ushort GetTotalSkillPointsForLevel(ushort level) =>
        (ushort)Math.Min(SkillCatalog.TotalLearnableRanks, Math.Max(1, (int)level));

    /// <summary>Computes unspent attribute points remaining for <paramref name="level"/> after <paramref name="state"/>.</summary>
    public static ushort GetUnspentStatPoints(ushort level, CharacterProgressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int total = GetTotalStatPointsForLevel(level);
        return (ushort)Math.Max(0, total - state.GetTotalSpentAttributePoints());
    }

    /// <summary>Computes unspent skill points remaining for <paramref name="level"/> after <paramref name="state"/>.</summary>
    public static ushort GetUnspentSkillPoints(ushort level, CharacterProgressionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int total = GetTotalSkillPointsForLevel(level);
        return (ushort)Math.Max(0, total - state.GetTotalSpentSkillPoints());
    }

    /// <summary>Returns total XP required to advance from <paramref name="level"/> to <c>level + 1</c>.</summary>
    public static uint GetXpToNextLevel(ushort level)
    {
        uint lv = Math.Max(1U, level);
        return 100U + ((lv - 1U) * 75U);
    }

    /// <summary>Computes derived stats for <paramref name="level"/> and armor slots.</summary>
    public static DerivedCharacterStats ComputeStats(ItemRules rules, ushort level, InventoryContainer inventory) =>
        ComputeStats(rules, level, inventory, selectedHotbarSlot: byte.MaxValue, progression: null);

    /// <summary>Computes derived stats combining level, armor slots, and active hotbar focus weapon.</summary>
    public static DerivedCharacterStats ComputeStats(ItemRules rules, ushort level, InventoryContainer inventory, byte selectedHotbarSlot) =>
        ComputeStats(rules, level, inventory, selectedHotbarSlot, progression: null);

    /// <summary>
    /// Computes derived HP, MP, Stamina, Defense, Offense, SpellPower, and MoveSpeed scaling combining
    /// character level, open attribute allocations, equipped armor slots (<c>36..39</c>), and held weapons.
    /// </summary>
    public static DerivedCharacterStats ComputeStats(
        ItemRules rules,
        ushort level,
        InventoryContainer inventory,
        byte selectedHotbarSlot,
        CharacterProgressionState? progression)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(inventory);

        ushort vit = progression?.GetAttribute(PrimaryAttributeId.Vitality) ?? 0;
        ushort wis = progression?.GetAttribute(PrimaryAttributeId.Wisdom) ?? 0;
        ushort pwr = progression?.GetAttribute(PrimaryAttributeId.Power) ?? 0;
        ushort agi = progression?.GetAttribute(PrimaryAttributeId.Agility) ?? 0;

        ushort lv = Math.Max((ushort)1, level);
        float maxHp = 100f + ((lv - 1) * 14f) + (vit * 12f);
        float maxMp = 80f + ((lv - 1) * 10f) + (wis * 10f);
        float maxStam = 100f + ((lv - 1) * 6f) + (agi * 8f);
        float defTotal = 2f + ((lv - 1) * 0.5f) + (vit * 0.5f);
        float atkMult = 1f + ((lv - 1) * 0.04f) + (pwr * 0.035f);
        float spellMult = 1f + ((lv - 1) * 0.045f) + (wis * 0.04f);
        float speedMult = MathF.Min(1.24f, 1f + (agi * 0.006f));

        for (int slotIdx = PlayerInventoryLayout.HeadSlot; slotIdx <= PlayerInventoryLayout.FeetSlot; slotIdx++)
        {
            if (slotIdx >= inventory.Capacity)
            {
                break;
            }

            ItemSlot armor = inventory.GetSlot(slotIdx);
            if (armor.IsEmpty || ToolUsagePolicy.IsWornOut(rules, in armor))
            {
                continue;
            }

            float upgMult = ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in armor));
            maxHp += rules.GetArmorBonusHealth(armor.ItemId) * upgMult;
            maxMp += rules.GetArmorBonusMana(armor.ItemId) * upgMult;
            defTotal += rules.GetArmorDefense(armor.ItemId) * upgMult;
        }

        if (selectedHotbarSlot < PlayerInventoryLayout.HotbarCount && selectedHotbarSlot < inventory.Capacity)
        {
            ItemSlot held = inventory.GetSlot(selectedHotbarSlot);
            if (!held.IsEmpty &&
                !ToolUsagePolicy.IsWornOut(rules, in held) &&
                rules.Items.TryGet(held.ItemId, out Definitions.ItemArchetype? heldDef) &&
                heldDef.Category == Definitions.ItemCategory.Weapon)
            {
                float upgMult = ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in held));
                if (heldDef.BonusHealth > 0f)
                {
                    maxHp += heldDef.BonusHealth * upgMult;
                }

                if (heldDef.BonusMana > 0f)
                {
                    maxMp += heldDef.BonusMana * upgMult;
                }

                if (heldDef.ArmorDefense > 0f)
                {
                    defTotal += heldDef.ArmorDefense * upgMult;
                }
            }
        }

        return new DerivedCharacterStats(maxHp, maxMp, maxStam, defTotal, atkMult, speedMult, spellMult);
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
