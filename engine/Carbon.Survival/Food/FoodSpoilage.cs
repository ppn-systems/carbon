// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;

namespace Carbon.Survival.Food;

/// <summary>
/// Pure spoilage step for one inventory slot (ADR-016). The host calls it on a slow cadence (about once a second)
/// for each slot holding a perishable food and applies the returned slot.
/// </summary>
public static class FoodSpoilage
{
    /// <summary>
    /// Advances <paramref name="slot"/> by <paramref name="elapsedSeconds"/> of online time.
    /// </summary>
    /// <param name="slot">Slot holding <paramref name="food"/>.</param>
    /// <param name="food">The slot's food definition.</param>
    /// <param name="elapsedSeconds">Seconds since the previous step.</param>
    /// <param name="spoiledItemId">Item id the stack turns into once spoiled (<c>0</c> removes it).</param>
    /// <returns>What happened, and the slot to store.</returns>
    public static SpoilageStep Advance(in ItemSlot slot, FoodDefinition food, float elapsedSeconds, uint spoiledItemId)
    {
        ArgumentNullException.ThrowIfNull(food);

        if (slot.IsEmpty || !food.IsPerishable || !(elapsedSeconds > 0f))
        {
            return new SpoilageStep(SpoilageOutcome.Unchanged, slot);
        }

        // A stack the server has not stamped yet (fresh loot, a craft or a legacy save) starts its countdown now.
        if (!slot.IsPerishable)
        {
            return new SpoilageStep(SpoilageOutcome.Stamped, slot with { FreshnessSeconds = food.SpoilSeconds });
        }

        float remaining = slot.FreshnessSeconds - elapsedSeconds;
        if (remaining > 0f)
        {
            return new SpoilageStep(SpoilageOutcome.Aging, slot with { FreshnessSeconds = remaining });
        }

        ItemSlot spoiled = spoiledItemId == 0U
            ? ItemSlot.Empty
            : new ItemSlot(spoiledItemId, slot.Quantity);
        return new SpoilageStep(SpoilageOutcome.Spoiled, spoiled);
    }
}
