// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Items.Containers;

namespace Carbon.Survival.Food;

/// <summary>
/// Outcome of one spoilage step and the slot to store.
/// </summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Slot">The slot after the step.</param>
public readonly record struct SpoilageStep(SpoilageOutcome Outcome, ItemSlot Slot);
