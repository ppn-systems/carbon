// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Economy.Currency;
using Carbon.Economy.Vendor;
using Carbon.Items.Components;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;

namespace Carbon.Economy.Crafting;

/// <summary>
/// Combines a craft with its station fee so ingredients, outputs and gold change together or not at all.
/// Wallets and inventories are only mutated on the world tick thread, so a fee checked with
/// <see cref="Wallet.CanAfford"/> is still payable after the inventory commit.
/// </summary>
public sealed class CraftingCheckout
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CraftingCheckout"/> class.
    /// </summary>
    /// <param name="prices">Price rules providing station fees and stack sizes.</param>
    public CraftingCheckout(PriceBook prices)
    {
        ArgumentNullException.ThrowIfNull(prices);
        this.Prices = prices;
    }

    /// <summary>Gets the price rules.</summary>
    public PriceBook Prices { get; }

    /// <summary>
    /// Crafts <paramref name="recipe"/> instantly and charges the station fee.
    /// </summary>
    /// <param name="wallet">Crafter wallet.</param>
    /// <param name="inventory">Crafter inventory.</param>
    /// <param name="recipe">Recipe.</param>
    /// <param name="context">Crafter state.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <param name="slotLimit">Only the first <paramref name="slotLimit"/> slots are used (e.g. the bag, not equipped armor).</param>
    /// <returns>The craft result; on failure nothing changed.</returns>
    public CraftResult TryCraft(Wallet wallet, InventoryContainer inventory, CraftingRecipe recipe, in CraftContext context, ulong tick = 0, int slotLimit = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        long fee = this.Prices.GetCraftingFee(recipe);
        if (!wallet.CanAfford(CurrencyKind.Gold, fee))
        {
            CraftResult requirements = RecipeMatcher.CheckRequirements(recipe, context);
            return requirements.IsOk ? CraftResult.Fail(CraftFailure.InsufficientFunds) : requirements;
        }

        CraftResult result = RecipeMatcher.TryCraft(inventory, recipe, this.Prices.Items, context, slotLimit);
        if (result.IsOk)
        {
            _ = wallet.TrySpend(CurrencyKind.Gold, fee, CurrencyReason.CraftingFee, tick);
        }

        return result;
    }

    /// <summary>
    /// Starts a timed craft: consumes ingredients, charges the station fee and returns the job to attach to the
    /// crafter entity. Outputs are delivered with <see cref="RecipeMatcher.TryDeliverOutputs"/> on completion.
    /// </summary>
    /// <param name="wallet">Crafter wallet.</param>
    /// <param name="inventory">Crafter inventory.</param>
    /// <param name="recipe">Recipe.</param>
    /// <param name="context">Crafter state (its skill level also sets the work speed).</param>
    /// <param name="job">The new job on success.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <returns>The craft result; on failure nothing changed.</returns>
    public CraftResult TryStartJob(
        Wallet wallet,
        InventoryContainer inventory,
        CraftingRecipe recipe,
        in CraftContext context,
        out CraftingJobComponent job,
        ulong tick = 0)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        job = default;
        long fee = this.Prices.GetCraftingFee(recipe);
        if (!wallet.CanAfford(CurrencyKind.Gold, fee))
        {
            CraftResult requirements = RecipeMatcher.CheckRequirements(recipe, context);
            return requirements.IsOk ? CraftResult.Fail(CraftFailure.InsufficientFunds) : requirements;
        }

        CraftResult result = RecipeMatcher.TryConsumeIngredients(inventory, recipe, this.Prices.Items, context);
        if (result.IsOk)
        {
            _ = wallet.TrySpend(CurrencyKind.Gold, fee, CurrencyReason.CraftingFee, tick);
            job = CraftingJobComponent.Start(recipe, context.SkillLevel);
        }

        return result;
    }
}
