// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Repair;

namespace Carbon.Items.Catalog;

/// <summary>The result of one wear step on an item (ADR-035).</summary>
/// <param name="Slot">The item after the step: worn, or <see cref="ItemSlot.Empty"/> when it broke.</param>
/// <param name="Changed">Whether the step changed the item at all (an item without durability never wears).</param>
/// <param name="Broke">Whether the step took the item to 0 durability, destroying it.</param>
/// <param name="ReachedCritical">Whether the step took the item from above to at or below the critical warning share.</param>
public readonly record struct DurabilityWearResult(ItemSlot Slot, bool Changed, bool Broke, bool ReachedCritical);

/// <summary>
/// The one wear rule every durable item follows (ADR-035): a step lowers durability, and the step that takes it to 0 or
/// below destroys the item. Crossing the critical share of <c>repair.json → durabilityWarning</c> is reported once per
/// descent, so the owner is warned before the break. Pure and allocation-free; the server applies the result.
/// </summary>
public static class DurabilityWear
{
    /// <summary>Applies <paramref name="loss"/> durability of wear to <paramref name="slot"/>.</summary>
    /// <param name="rules">Item rules (definitions and the repair table's warning shares).</param>
    /// <param name="slot">The worn item.</param>
    /// <param name="loss">Durability to remove; nothing happens when it is not positive.</param>
    /// <returns>The worn or broken item and what the step crossed.</returns>
    public static DurabilityWearResult Apply(ItemRules rules, in ItemSlot slot, float loss)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (loss <= 0f ||
            slot.IsEmpty ||
            !rules.Items.TryGet(slot.ItemId, out ItemArchetype? def) ||
            def.MaxDurability <= 0f)
        {
            return new DurabilityWearResult(slot, Changed: false, Broke: false, ReachedCritical: false);
        }

        float next = slot.Durability - loss;
        if (next <= 0f)
        {
            return new DurabilityWearResult(ItemSlot.Empty, Changed: true, Broke: true, ReachedCritical: false);
        }

        float max = ItemEnhancementPolicy.GetEffectiveMaxDurability(def, ItemEnhancementPolicy.GetUpgradeLevel(in slot));
        bool reachedCritical =
            rules.Repair.GetDurabilityBand(slot.Durability, max) != DurabilityBand.Critical &&
            rules.Repair.GetDurabilityBand(next, max) == DurabilityBand.Critical;
        return new DurabilityWearResult(slot with { Durability = next }, Changed: true, Broke: false, reachedCritical);
    }

    /// <summary>
    /// Durability a legacy item saved at 0 (the retired "worn out" state) comes back with on load (ADR-035): one point,
    /// so it survives to be repaired but breaks on its next wear step.
    /// </summary>
    public const float LegacyRestoredDurability = 1f;

    /// <summary>
    /// Gives every durable item in <paramref name="inventory"/> saved at 0 or less (ADR-035 retired that state) back
    /// <see cref="LegacyRestoredDurability"/> durability, capped at its effective maximum. Items without durability are left alone.
    /// </summary>
    /// <param name="rules">Item rules.</param>
    /// <param name="inventory">A freshly loaded inventory.</param>
    /// <returns>How many items were restored (the caller logs and persists them).</returns>
    public static int RestoreLegacyBrokenItems(ItemRules rules, InventoryContainer inventory)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(inventory);
        int restored = 0;
        for (int i = 0; i < inventory.Capacity; i++)
        {
            ItemSlot slot = inventory.GetSlot(i);
            if (slot.IsEmpty ||
                slot.Durability > 0f ||
                !rules.Items.TryGet(slot.ItemId, out ItemArchetype? def) ||
                def.MaxDurability <= 0f)
            {
                continue;
            }

            float max = ItemEnhancementPolicy.GetEffectiveMaxDurability(def, ItemEnhancementPolicy.GetUpgradeLevel(in slot));
            inventory.SetSlot(i, slot with { Durability = MathF.Min(LegacyRestoredDurability, max) });
            restored++;
        }

        return restored;
    }
}
