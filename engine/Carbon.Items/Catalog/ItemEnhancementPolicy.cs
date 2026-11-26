// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Items.Catalog;

/// <summary>
/// Governs forge enhancement levels (<c>+0..+10</c>) stored on <see cref="ItemSlot.UpgradeLevel"/>,
/// stat scaling multipliers, effective max durability, and upgrade material recipes.
/// </summary>
public static class ItemEnhancementPolicy
{
    /// <summary>Maximum enhancement level achievable through forging.</summary>
    public const byte MaxUpgradeLevel = 10;

    /// <summary>Extracts the item's enhancement level (<c>0..10</c>) from <paramref name="slot"/>.</summary>
    public static byte GetUpgradeLevel(in ItemSlot slot) =>
        slot.IsEmpty ? (byte)0 : Math.Clamp(slot.UpgradeLevel, (byte)0, MaxUpgradeLevel);

    /// <summary>Returns a copy of <paramref name="slot"/> with <paramref name="upgradeLevel"/> updated cleanly.</summary>
    public static ItemSlot WithUpgradeLevel(in ItemSlot slot, byte upgradeLevel) =>
        slot with { UpgradeLevel = Math.Clamp(upgradeLevel, (byte)0, MaxUpgradeLevel) };

    /// <summary>Returns the weapon/harvest/defense multiplier for <paramref name="upgradeLevel"/> (<c>+12%</c> per level).</summary>
    public static float GetStatMultiplier(byte upgradeLevel) =>
        1f + (Math.Clamp(upgradeLevel, (byte)0, MaxUpgradeLevel) * 0.12f);

    /// <summary>Returns effective maximum durability for <paramref name="archetype"/> scaled by <paramref name="upgradeLevel"/>.</summary>
    public static float GetEffectiveMaxDurability(ItemArchetype archetype, byte upgradeLevel)
    {
        ArgumentNullException.ThrowIfNull(archetype);
        if (archetype.MaxDurability <= 0f)
        {
            return 0f;
        }

        return archetype.MaxDurability * (1f + (Math.Clamp(upgradeLevel, (byte)0, MaxUpgradeLevel) * 0.10f));
    }

    /// <summary>
    /// Checks whether <paramref name="slot"/> is eligible for forging and outputs its material costs &amp; success chance.
    /// </summary>
    public static bool CanUpgrade(
        ItemRules rules,
        in ItemSlot slot,
        out uint material1Id,
        out int material1Count,
        out uint material2Id,
        out int material2Count,
        out float successChance)
    {
        ArgumentNullException.ThrowIfNull(rules);
        material1Id = 0U;
        material1Count = 0;
        material2Id = 0U;
        material2Count = 0;
        successChance = 0f;

        if (slot.IsEmpty || !rules.Items.TryGet(slot.ItemId, out ItemArchetype? def) || def.MaxDurability <= 0f)
        {
            return false;
        }

        byte level = GetUpgradeLevel(in slot);
        if (level >= MaxUpgradeLevel)
        {
            return false;
        }

        if (level < 3)
        {
            material1Id = StarterGameCatalog.StoneChunk;
            material1Count = 2 + level;
            material2Id = StarterGameCatalog.IronOre;
            material2Count = 1 + level;
            successChance = 1.0f;
        }
        else if (level < 6)
        {
            material1Id = StarterGameCatalog.IronOre;
            material1Count = level;
            material2Id = StarterGameCatalog.ManaCrystal;
            material2Count = level - 2;
            successChance = 0.85f;
        }
        else
        {
            material1Id = StarterGameCatalog.IronOre;
            material1Count = level + 1;
            material2Id = StarterGameCatalog.ManaCrystal;
            material2Count = level - 3;
            successChance = 0.70f;
        }

        return true;
    }
}
