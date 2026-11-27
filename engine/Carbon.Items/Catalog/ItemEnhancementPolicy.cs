// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Enhancement;

namespace Carbon.Items.Catalog;

/// <summary>
/// Governs forge enhancement levels (<c>+0..+10</c>) stored on <see cref="ItemSlot.UpgradeLevel"/>,
/// stat scaling multipliers, effective max durability, and the lookups into the data-driven attempt table
/// (<see cref="EnhancementTable"/>, <c>enhancement.json</c>).
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

    /// <summary>Returns whether <paramref name="itemId"/> is gear the forge can enhance (it has durability).</summary>
    /// <param name="rules">Item rules.</param>
    /// <param name="itemId">Item id.</param>
    /// <returns><see langword="true"/> for weapons, tools and armor with durability.</returns>
    public static bool IsEnhanceable(ItemRules rules, uint itemId)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return itemId != 0U && rules.Items.TryGet(itemId, out ItemArchetype? def) && def.MaxDurability > 0f;
    }

    /// <summary>
    /// Gets the next forge attempt for <paramref name="slot"/> from the data-driven table (<c>enhancement.json</c>,
    /// ADR-024): outcome weights, materials and base gold fee.
    /// </summary>
    /// <param name="rules">Item rules holding the enhancement table.</param>
    /// <param name="slot">Item to enhance.</param>
    /// <param name="step">The attempt row when the item can still be enhanced.</param>
    /// <returns><see langword="false"/> for empty slots, non-gear, or items at the table's max level.</returns>
    public static bool TryGetStep(ItemRules rules, in ItemSlot slot, [NotNullWhen(true)] out EnhancementStep? step)
    {
        ArgumentNullException.ThrowIfNull(rules);
        step = null;
        return !slot.IsEmpty
            && IsEnhanceable(rules, slot.ItemId)
            && rules.Enhancement.TryGetStep(GetUpgradeLevel(in slot), out step);
    }

    /// <summary>Returns the gold fee of <paramref name="step"/> for the item in <paramref name="slot"/> (scaled by its rarity).</summary>
    /// <param name="rules">Item rules.</param>
    /// <param name="slot">Item to enhance.</param>
    /// <param name="step">Attempt row.</param>
    /// <returns>The fee in gold.</returns>
    public static long GetGoldFee(ItemRules rules, in ItemSlot slot, EnhancementStep step)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ItemRarity rarity = rules.ItemCatalog.TryGet(slot.ItemId, out ItemCatalogEntry? entry) ? entry.Rarity : ItemRarity.Common;
        return rules.Enhancement.GetGoldFee(step, rarity);
    }
}
