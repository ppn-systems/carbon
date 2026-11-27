// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Items.Progression;

/// <summary>
/// Derived character stats from synced data only: <c>progression.json</c> (<see cref="ProgressionConfig"/>) and the
/// equipped items' stats from <c>items.json</c> (<see cref="ItemRules"/>). The server and the client's stat preview
/// call the same code, so the preview always matches what the server will compute.
/// </summary>
public static class CharacterStatCalculator
{
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
    public static DerivedCharacterStats Compute(
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
                rules.Items.TryGet(held.ItemId, out ItemArchetype? heldDef) &&
                heldDef.Category == ItemCategory.Weapon)
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
}
