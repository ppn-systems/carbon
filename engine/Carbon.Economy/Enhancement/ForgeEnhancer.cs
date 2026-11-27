// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Economy.Currency;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Enhancement;
using Carbon.Primitives.Random;

namespace Carbon.Economy.Enhancement;

/// <summary>
/// Runs one forge enhancement attempt atomically (ADR-024): every precondition (gear, level, materials, gold, scroll) is
/// checked before anything is spent; then the gold fee, the materials and the scroll are taken exactly once, the server
/// rolls on its <see cref="DeterministicRandom"/> stream, and the outcome is written to the slot. The caller holds the
/// world lock, persists and replicates. Clients never roll.
/// </summary>
public static class ForgeEnhancer
{
    /// <summary>
    /// Derives the roll stream of one attempt from the world seed, the tick, the character and a per-world attempt counter,
    /// so two attempts never share a stream and a replay of the same inputs rolls the same outcome.
    /// </summary>
    /// <param name="worldSeed">World seed (server-only).</param>
    /// <param name="tick">Current server tick.</param>
    /// <param name="ownerId">Character or wallet owner id.</param>
    /// <param name="attemptNonce">Monotonic per-world attempt counter.</param>
    /// <returns>The roll stream.</returns>
    public static DeterministicRandom CreateRandom(ulong worldSeed, ulong tick, ulong ownerId, ulong attemptNonce)
    {
        ulong mixed = worldSeed
            ^ (tick * 0x9E3779B97F4A7C15UL)
            ^ ((ownerId + 0x632BE59BD9B4E019UL) * 0xC2B2AE3D27D4EB4FUL)
            ^ ((attemptNonce + 0x165667B19E3779F9UL) * 0xD6E8FEB86659FD93UL);
        return new DeterministicRandom(mixed);
    }

    /// <summary>Attempts to enhance the item in <paramref name="slotIndex"/>.</summary>
    /// <param name="rules">Item rules holding the enhancement table.</param>
    /// <param name="wallet">Wallet charged the gold fee.</param>
    /// <param name="inventory">Inventory holding the item, materials and scroll.</param>
    /// <param name="slotIndex">Slot of the item.</param>
    /// <param name="protectionItemId">Scroll to spend, or <c>0</c>.</param>
    /// <param name="random">Server roll stream.</param>
    /// <param name="tick">Tick recorded on the wallet ledger.</param>
    /// <returns>What happened.</returns>
    public static ForgeAttempt TryEnhance(
        ItemRules rules,
        Wallet wallet,
        InventoryContainer inventory,
        int slotIndex,
        uint protectionItemId,
        ref DeterministicRandom random,
        ulong tick = 0)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(inventory);
        if ((uint)slotIndex >= (uint)inventory.Capacity)
        {
            return ForgeAttempt.Rejected(OpStatus.Invalid);
        }

        ItemSlot target = inventory.GetSlot(slotIndex);
        if (!ItemEnhancementPolicy.TryGetStep(rules, in target, out EnhancementStep? step))
        {
            return ForgeAttempt.Rejected(OpStatus.Invalid);
        }

        EnhancementProtection? protection = null;
        if (protectionItemId != 0U)
        {
            if (!rules.Enhancement.TryGetProtection(protectionItemId, out EnhancementProtection scroll) || !scroll.AppliesTo(step))
            {
                return ForgeAttempt.Rejected(OpStatus.Invalid);
            }

            protection = scroll;
        }

        // Every check before any spend, so a rejection changes nothing.
        ReadOnlySpan<RecipeIngredient> materials = step.MaterialSpan;
        for (int i = 0; i < materials.Length; i++)
        {
            int needed = materials[i].Quantity + (materials[i].ItemId == protectionItemId ? 1 : 0);
            if (inventory.CountItem(materials[i].ItemId) < needed)
            {
                return ForgeAttempt.Rejected(OpStatus.Conflict);
            }
        }

        if (protectionItemId != 0U && inventory.CountItem(protectionItemId) < 1)
        {
            return ForgeAttempt.Rejected(OpStatus.Conflict);
        }

        long fee = ItemEnhancementPolicy.GetGoldFee(rules, in target, step);
        if (!wallet.CanAfford(CurrencyKind.Gold, fee))
        {
            return ForgeAttempt.Rejected(OpStatus.Conflict);
        }

        if (fee > 0 && !wallet.TrySpend(CurrencyKind.Gold, fee, CurrencyReason.Enhancement, tick).IsOk)
        {
            return ForgeAttempt.Rejected(OpStatus.Conflict);
        }

        for (int i = 0; i < materials.Length; i++)
        {
            _ = inventory.TryRemove(materials[i].ItemId, materials[i].Quantity);
        }

        if (protectionItemId != 0U)
        {
            _ = inventory.TryRemove(protectionItemId, 1);
        }

        byte previous = step.FromLevel;
        EnhancementOutcome outcome = EnhancementTable.Roll(step, ref random, protection);
        byte next = outcome switch
        {
            EnhancementOutcome.Success => (byte)(previous + 1),
            EnhancementOutcome.Downgrade => (byte)(previous - 1),
            EnhancementOutcome.Destroyed => 0,
            EnhancementOutcome.None or EnhancementOutcome.NoChange => previous,
            _ => previous,
        };

        ApplyOutcome(rules, inventory, slotIndex, in target, outcome, next);
        return new ForgeAttempt(OpStatus.Ok, outcome, target.ItemId, previous, next, fee, protectionItemId != 0U);
    }

    private static void ApplyOutcome(ItemRules rules, InventoryContainer inventory, int slotIndex, in ItemSlot target, EnhancementOutcome outcome, byte next)
    {
        switch (outcome)
        {
            case EnhancementOutcome.Destroyed:
                inventory.SetSlot(slotIndex, ItemSlot.Empty);
                break;
            case EnhancementOutcome.Success:
            case EnhancementOutcome.Downgrade:
                float maxDurability = rules.Items.TryGet(target.ItemId, out ItemArchetype? def)
                    ? ItemEnhancementPolicy.GetEffectiveMaxDurability(def, next)
                    : target.Durability;

                // A success restores the item to its new, higher maximum; a downgrade only caps it at the lower one.
                float durability = outcome == EnhancementOutcome.Success ? maxDurability : MathF.Min(target.Durability, maxDurability);
                inventory.SetSlot(slotIndex, ItemEnhancementPolicy.WithUpgradeLevel(in target, next) with { Durability = durability });
                break;
            case EnhancementOutcome.None:
            case EnhancementOutcome.NoChange:
            default:
                break;
        }
    }
}
