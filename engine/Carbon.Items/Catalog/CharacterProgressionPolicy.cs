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
/// Allocatable player attribute points and skill mastery ranks (<c>0..5</c>).
/// </summary>
public readonly record struct CharacterAllocationPoints(
    ushort Vit = 0,
    ushort Wis = 0,
    ushort Pwr = 0,
    ushort Agi = 0,
    byte Skill1Rank = 0,
    byte Skill2Rank = 0,
    byte Skill3Rank = 0)
{
    /// <summary>Gets the learned rank (<c>0</c>=locked, <c>1..5</c>) for <paramref name="skillSlot"/>.</summary>
    public byte GetSkillRank(byte skillSlot) => skillSlot switch
    {
        CharacterProgressionPolicy.SkillWhirlwind => this.Skill1Rank,
        CharacterProgressionPolicy.SkillArcaneNova => this.Skill2Rank,
        CharacterProgressionPolicy.SkillVitalSurge => this.Skill3Rank,
        _ => 0,
    };
}

/// <summary>
/// Central progression policy governing character leveling curves, attribute &amp; skill point rewards,
/// armor stat aggregation, damage mitigation, stamina/mana economy, and active skill scaling.
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

    /// <summary>Maximum mastery rank per active skill.</summary>
    public const byte MaxSkillRank = 5;

    /// <summary>Attribute points granted upon each level up above Level 1.</summary>
    public const int StatPointsPerLevel = 4;

    /// <summary>Returns cumulative attribute points earned by <paramref name="level"/>.</summary>
    public static ushort GetTotalStatPointsForLevel(ushort level) =>
        (ushort)(Math.Max(0, level - 1) * StatPointsPerLevel);

    /// <summary>Returns cumulative skill mastery points earned by <paramref name="level"/> (<c>1 SP</c> at Lv.1, <c>+1</c> per level).</summary>
    public static ushort GetTotalSkillPointsForLevel(ushort level) =>
        Math.Max((ushort)1, level);

    /// <summary>Computes unspent attribute points remaining for <paramref name="level"/> after <paramref name="pts"/>.</summary>
    public static ushort GetUnspentStatPoints(ushort level, in CharacterAllocationPoints pts)
    {
        int total = GetTotalStatPointsForLevel(level);
        int spent = pts.Vit + pts.Wis + pts.Pwr + pts.Agi;
        return (ushort)Math.Max(0, total - spent);
    }

    /// <summary>Computes unspent skill points remaining for <paramref name="level"/> after <paramref name="pts"/>.</summary>
    public static ushort GetUnspentSkillPoints(ushort level, in CharacterAllocationPoints pts)
    {
        int total = GetTotalSkillPointsForLevel(level);
        int spent = pts.Skill1Rank + pts.Skill2Rank + pts.Skill3Rank;
        return (ushort)Math.Max(0, total - spent);
    }

    /// <summary>Returns total XP required to advance from <paramref name="level"/> to <c>level + 1</c>.</summary>
    public static uint GetXpToNextLevel(ushort level)
    {
        uint lv = Math.Max(1U, level);
        return 100U + ((lv - 1U) * 75U);
    }

    /// <summary>Computes derived HP, MP, Stamina, Defense, and Offense scaling for <paramref name="level"/> and armor slots.</summary>
    public static DerivedCharacterStats ComputeStats(ushort level, InventoryContainer inventory) =>
        ComputeStats(level, inventory, selectedHotbarSlot: byte.MaxValue, default);

    /// <summary>
    /// Computes derived HP, MP, Stamina, Defense, and Offense scaling for <paramref name="level"/>,
    /// equipped armor slots (<c>36..39</c>), and an actively held hotbar focus weapon (such as Mage Staffs).
    /// </summary>
    public static DerivedCharacterStats ComputeStats(ushort level, InventoryContainer inventory, byte selectedHotbarSlot) =>
        ComputeStats(level, inventory, selectedHotbarSlot, default);

    /// <summary>
    /// Computes derived HP, MP, Stamina, Defense, Offense, and MoveSpeed scaling combining level,
    /// allocated attribute points, equipped armor slots (<c>36..39</c>), and held focus weapons.
    /// </summary>
    public static DerivedCharacterStats ComputeStats(
        ushort level,
        InventoryContainer inventory,
        byte selectedHotbarSlot,
        in CharacterAllocationPoints points)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        ushort lv = Math.Max((ushort)1, level);
        float maxHp = 100f + ((lv - 1) * 14f) + (points.Vit * 12f);
        float maxMp = 80f + ((lv - 1) * 10f) + (points.Wis * 10f);
        float maxStam = 100f + ((lv - 1) * 6f) + (points.Agi * 8f);
        float defTotal = 2f + ((lv - 1) * 0.5f) + (points.Vit * 0.5f);
        float atkMult = 1f + ((lv - 1) * 0.04f) + (points.Pwr * 0.035f);
        float speedMult = MathF.Min(1.24f, 1f + (points.Agi * 0.006f));

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

        if (selectedHotbarSlot < PlayerInventoryLayout.HotbarCount && selectedHotbarSlot < inventory.Capacity)
        {
            ItemSlot held = inventory.GetSlot(selectedHotbarSlot);
            if (!held.IsEmpty &&
                !ToolUsagePolicy.IsWornOut(in held) &&
                StarterGameCatalog.Items.TryGet(held.ItemId, out Definitions.ItemArchetype? heldDef) &&
                heldDef.Category == Definitions.ItemCategory.Weapon &&
                heldDef.BonusMana > 0f)
            {
                float upgMult = ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in held));
                maxMp += heldDef.BonusMana * upgMult;
            }
        }

        return new DerivedCharacterStats(maxHp, maxMp, maxStam, defTotal, atkMult, speedMult);
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

    /// <summary>Resolves baseline Rank 1 combat &amp; resource specs for <paramref name="skillSlot"/> (<c>1..3</c>).</summary>
    public static bool TryGetSkillSpec(byte skillSlot, out SkillSpec spec) =>
        TryGetSkillSpec(skillSlot, rank: 1, out spec);

    /// <summary>Resolves rank-scaled combat &amp; resource specs for <paramref name="skillSlot"/> (<c>1..3</c>) and <paramref name="rank"/> (<c>1..5</c>).</summary>
    public static bool TryGetSkillSpec(byte skillSlot, byte rank, out SkillSpec spec)
    {
        SkillSpec baseSpec = skillSlot switch
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

        if (baseSpec.SkillSlot == 0)
        {
            spec = default;
            return false;
        }

        byte effRank = Math.Clamp(rank, (byte)1, MaxSkillRank);
        int bonusLevels = effRank - 1;
        spec = baseSpec with
        {
            ManaCost = baseSpec.ManaCost + (bonusLevels * 3f),
            CooldownSeconds = MathF.Max(2.2f, baseSpec.CooldownSeconds - (bonusLevels * 0.40f)),
            DamageMultiplier = baseSpec.DamageMultiplier > 0f
                ? baseSpec.DamageMultiplier * (1f + (bonusLevels * 0.16f))
                : 0f,
            SelfHealRatio = baseSpec.SelfHealRatio > 0f
                ? baseSpec.SelfHealRatio + (bonusLevels * 0.04f)
                : 0f,
            StaminaRestore = baseSpec.StaminaRestore > 0f
                ? baseSpec.StaminaRestore + (bonusLevels * 6f)
                : 0f,
        };
        return true;
    }

    /// <summary>Returns MP cost, Stamina cost, cooldown seconds, and base name for <paramref name="skillSlot"/> (<c>1..3</c>).</summary>
    public static bool TryGetSkillSpec(
        byte skillSlot,
        out string displayName,
        out float manaCost,
        out float staminaCost,
        out float cooldownSeconds)
    {
        if (TryGetSkillSpec(skillSlot, rank: 1, out SkillSpec spec))
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
