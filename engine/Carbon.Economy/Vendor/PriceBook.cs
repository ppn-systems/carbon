// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Economy.Config;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;

namespace Carbon.Economy.Vendor;

/// <summary>
/// Integer price rules derived from item base prices and <see cref="EconomyConfig"/>: vendor buy/sell spread,
/// repair costs and crafting station fees (the gold sinks).
/// </summary>
public sealed class PriceBook
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PriceBook"/> class.
    /// </summary>
    /// <param name="items">Item catalog.</param>
    /// <param name="config">Economy tunables.</param>
    public PriceBook(ItemCatalog items, EconomyConfig config)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(config);
        this.Items = items;
        this.Config = config;
    }

    /// <summary>Gets the item catalog.</summary>
    public ItemCatalog Items { get; }

    /// <summary>Gets the economy tunables.</summary>
    public EconomyConfig Config { get; }

    /// <summary>
    /// Gets the unit price a vendor charges, or <c>-1</c> when vendors do not sell the item.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="buyerLevel">Buyer level; consumables are priced by <c>vendor.consumablePriceBands</c> (0 = base price).</param>
    /// <returns>Unit buy price.</returns>
    public long GetBuyPrice(uint itemId, int buyerLevel = 0)
    {
        if (!this.Items.TryGet(itemId, out ItemCatalogEntry? item) || !item.VendorBuyable)
        {
            return -1;
        }

        long percent = this.Config.Vendor.BuyMarkupPercent;
        if (item.Category == ItemCategory.Consumable)
        {
            percent = percent * this.Config.GetConsumablePricePercent(buyerLevel) / 100;
        }

        long price = CeilDiv(item.BasePrice * percent, 100);
        return item.BasePrice > 0 ? Math.Max(1, price) : 0;
    }

    /// <summary>
    /// Gets the unit price a vendor pays (always below the buy price), or <c>0</c> for unknown items.
    /// </summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>Unit sell price.</returns>
    public long GetSellPrice(uint itemId) =>
        this.Items.TryGet(itemId, out ItemCatalogEntry? item)
            ? item.BasePrice * this.Config.Vendor.SellRatioPercent / 100
            : 0;

    /// <summary>
    /// Gets the gold cost of restoring <paramref name="slot"/> to full durability (<c>0</c> when nothing to repair).
    /// </summary>
    /// <param name="slot">Equipment slot.</param>
    /// <returns>Repair cost.</returns>
    public long GetRepairCost(in ItemSlot slot)
    {
        if (slot.IsEmpty || !this.Items.TryGet(slot.ItemId, out ItemCatalogEntry? item) || item.MaxDurability <= 0f)
        {
            return 0;
        }

        float missing = Math.Clamp(item.MaxDurability - slot.Durability, 0f, item.MaxDurability);
        if (missing <= 0f)
        {
            return 0;
        }

        long missingPermille = Math.Clamp((long)MathF.Ceiling(missing * 1000f / item.MaxDurability), 1L, 1000L);
        long percent = (long)this.Config.Repair.CostPercentOfBasePrice * this.Config.Repair.RarityMultiplierPercent.For(item.Rarity);
        long cost = CeilDiv(item.BasePrice * percent * missingPermille, 100L * 100L * 1000L);
        return Math.Max(this.Config.Repair.MinimumCost, cost);
    }

    /// <summary>
    /// Gets the base value of every output of <paramref name="recipe"/>.
    /// </summary>
    /// <param name="recipe">Recipe.</param>
    /// <returns>Sum of <c>basePrice × quantity</c>.</returns>
    public long GetOutputValue(CraftingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        long total = 0;
        for (int i = 0; i < recipe.OutputCount; i++)
        {
            ItemSlot output = recipe.GetOutput(i);
            if (this.Items.TryGet(output.ItemId, out ItemCatalogEntry? item))
            {
                total += item.BasePrice * output.Quantity;
            }
        }

        return total;
    }

    /// <summary>
    /// Gets the base value of every ingredient of <paramref name="recipe"/>.
    /// </summary>
    /// <param name="recipe">Recipe.</param>
    /// <returns>Sum of <c>basePrice × quantity</c>.</returns>
    public long GetIngredientValue(CraftingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        long total = 0;
        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            RecipeIngredient ingredient = recipe.Ingredients[i];
            if (this.Items.TryGet(ingredient.ItemId, out ItemCatalogEntry? item))
            {
                total += item.BasePrice * ingredient.Quantity;
            }
        }

        return total;
    }

    /// <summary>
    /// Gets the gold fee charged for crafting <paramref name="recipe"/> at its station.
    /// </summary>
    /// <param name="recipe">Recipe.</param>
    /// <returns>Flat station fee plus the value-proportional fee (stations only).</returns>
    public long GetCraftingFee(CraftingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        CraftingFeeRules fees = this.Config.CraftingFees;
        long flat = recipe.Station switch
        {
            CraftingStation.None => fees.None,
            CraftingStation.Workbench => fees.Workbench,
            CraftingStation.Forge => fees.Forge,
            CraftingStation.Stove => fees.Stove,
            CraftingStation.Alchemy => fees.Alchemy,
            _ => 0
        };

        if (recipe.Station == CraftingStation.None)
        {
            return flat;
        }

        long proportional = this.GetOutputValue(recipe) * fees.OutputValueRateBps / 10_000;
        return (flat + proportional) * fees.RarityMultiplierPercent.For(this.GetRarestOutput(recipe)) / 100;
    }

    private ItemRarity GetRarestOutput(CraftingRecipe recipe)
    {
        ItemRarity rarest = ItemRarity.Common;
        for (int i = 0; i < recipe.OutputCount; i++)
        {
            if (this.Items.TryGet(recipe.GetOutput(i).ItemId, out ItemCatalogEntry? item) && item.Rarity > rarest)
            {
                rarest = item.Rarity;
            }
        }

        return rarest;
    }

    private static long CeilDiv(long value, long divisor) => (value + divisor - 1) / divisor;
}
