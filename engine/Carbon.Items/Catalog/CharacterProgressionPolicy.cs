// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;
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
/// Character stat aggregation (gear + <see cref="ProgressionConfig"/>), skill point rewards and active skill evaluation via
/// <see cref="SkillCatalog"/>. Experience, attribute points, stat growth and mitigation come from <c>progression.json</c>
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

    /// <summary>
    /// Computes derived stats from <paramref name="config"/> (<c>progression.json</c>): base and per-level growth,
    /// attribute allocations, equipped armor (slots <c>36..39</c>) and the held weapon, with caps applied.
    /// </summary>
    /// <param name="rules">Item rules (armor and weapon stats).</param>
    /// <param name="config">Progression tuning.</param>
    /// <param name="level">Character level.</param>
    /// <param name="inventory">Inventory with equipment slots.</param>
    /// <param name="selectedHotbarSlot">Held hotbar slot (<see cref="byte.MaxValue"/> = none).</param>
    /// <param name="progression">Attribute allocations.</param>
    /// <returns>The derived stats.</returns>
    public static DerivedCharacterStats ComputeStats(
        ItemRules rules,
        ProgressionConfig config,
        ushort level,
        InventoryContainer inventory,
        byte selectedHotbarSlot,
        CharacterProgressionState? progression)
    {
        ArgumentNullException.ThrowIfNull(config);
        GearStatBonus gear = SumGear(rules, inventory, selectedHotbarSlot);
        return config.ComputeStats(level, progression, in gear);
    }

    /// <summary>
    /// Sums the bonuses of equipped, non-broken armor (slots <c>36..39</c>) and the held weapon, enhancement included.
    /// </summary>
    /// <param name="rules">Item rules.</param>
    /// <param name="inventory">Inventory with equipment slots.</param>
    /// <param name="selectedHotbarSlot">Held hotbar slot (<see cref="byte.MaxValue"/> = none).</param>
    /// <returns>The summed gear bonuses.</returns>
    public static GearStatBonus SumGear(ItemRules rules, InventoryContainer inventory, byte selectedHotbarSlot)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(inventory);

        float hp = 0f;
        float mp = 0f;
        float def = 0f;
        float crit = 0f;
        for (int slotIdx = PlayerInventoryLayout.HeadSlot; slotIdx <= PlayerInventoryLayout.FeetSlot && slotIdx < inventory.Capacity; slotIdx++)
        {
            ItemSlot armor = inventory.GetSlot(slotIdx);
            if (armor.IsEmpty || ToolUsagePolicy.IsWornOut(rules, in armor))
            {
                continue;
            }

            float upgMult = ItemEnhancementPolicy.GetStatMultiplier(ItemEnhancementPolicy.GetUpgradeLevel(in armor));
            hp += rules.GetArmorBonusHealth(armor.ItemId) * upgMult;
            mp += rules.GetArmorBonusMana(armor.ItemId) * upgMult;
            def += rules.GetArmorDefense(armor.ItemId) * upgMult;
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
                hp += MathF.Max(0f, heldDef.BonusHealth) * upgMult;
                mp += MathF.Max(0f, heldDef.BonusMana) * upgMult;
                def += MathF.Max(0f, heldDef.ArmorDefense) * upgMult;
                crit += MathF.Max(0f, heldDef.CritChance);
            }
        }

        return new GearStatBonus(hp, mp, def, crit);
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
