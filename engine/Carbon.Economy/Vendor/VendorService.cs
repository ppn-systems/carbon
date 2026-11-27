// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Economy.Currency;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;

namespace Carbon.Economy.Vendor;

/// <summary>
/// Server-authoritative NPC vendor and repair operations. Every operation validates funds, wallet cap and inventory
/// space up front and then commits wallet and inventory together, so no request can half-complete.
/// </summary>
public sealed class VendorService
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VendorService"/> class.
    /// </summary>
    /// <param name="prices">Price rules.</param>
    public VendorService(PriceBook prices)
    {
        ArgumentNullException.ThrowIfNull(prices);
        this.Prices = prices;
    }

    /// <summary>Gets the price rules.</summary>
    public PriceBook Prices { get; }

    /// <summary>
    /// Buys <paramref name="quantity"/> units from a vendor.
    /// </summary>
    /// <param name="wallet">Buyer wallet.</param>
    /// <param name="inventory">Buyer inventory.</param>
    /// <param name="itemId">Item to buy.</param>
    /// <param name="quantity">Positive quantity.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <param name="slotLimit">Only the first <paramref name="slotLimit"/> slots receive items (e.g. the bag, not equipment).</param>
    /// <param name="buyerLevel">Buyer level, for level-banded consumable prices (0 = base price).</param>
    /// <returns>The outcome; on failure nothing changed.</returns>
    public EconomyResult TryBuy(Wallet wallet, InventoryContainer inventory, uint itemId, int quantity, ulong tick = 0, int slotLimit = int.MaxValue, int buyerLevel = 0)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(inventory);
        if (quantity <= 0)
        {
            return EconomyResult.Fail(EconomyError.InvalidAmount);
        }

        if (!this.Prices.Items.TryGet(itemId, out ItemCatalogEntry? item))
        {
            return EconomyResult.Fail(EconomyError.UnknownItem);
        }

        long unit = this.Prices.GetBuyPrice(itemId, buyerLevel);
        if (unit < 0)
        {
            return EconomyResult.Fail(EconomyError.NotForSale);
        }

        if (!TryMultiply(unit, quantity, out long total))
        {
            return EconomyResult.Fail(EconomyError.InvalidAmount);
        }

        if (!wallet.CanAfford(CurrencyKind.Gold, total))
        {
            return EconomyResult.Fail(EconomyError.InsufficientFunds);
        }

        Span<ItemSlot> buffer = inventory.Capacity <= InventoryDraft.StackBufferLimit
            ? stackalloc ItemSlot[InventoryDraft.StackBufferLimit]
            : new ItemSlot[inventory.Capacity];
        InventoryDraft draft = InventoryDraft.Begin(inventory, buffer, slotLimit);
        if (draft.Add(new ItemSlot(itemId, quantity, Durability: item.MaxDurability), item.MaxStack) != 0)
        {
            return EconomyResult.Fail(EconomyError.InventoryFull);
        }

        EconomyResult paid = wallet.TrySpend(CurrencyKind.Gold, total, CurrencyReason.VendorPurchase, tick);
        if (paid.IsOk)
        {
            draft.CommitTo(inventory);
        }

        return paid;
    }

    /// <summary>
    /// Sells <paramref name="quantity"/> units to a vendor at the spread-reduced sell price.
    /// </summary>
    /// <param name="wallet">Seller wallet.</param>
    /// <param name="inventory">Seller inventory.</param>
    /// <param name="itemId">Item to sell.</param>
    /// <param name="quantity">Positive quantity.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <returns>The outcome; on failure nothing changed.</returns>
    public EconomyResult TrySell(Wallet wallet, InventoryContainer inventory, uint itemId, int quantity, ulong tick = 0)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(inventory);
        if (quantity <= 0)
        {
            return EconomyResult.Fail(EconomyError.InvalidAmount);
        }

        if (!this.Prices.Items.TryGet(itemId, out _))
        {
            return EconomyResult.Fail(EconomyError.UnknownItem);
        }

        long unit = this.Prices.GetSellPrice(itemId);
        if (unit <= 0)
        {
            return EconomyResult.Fail(EconomyError.NotForSale);
        }

        if (inventory.CountItem(itemId) < quantity)
        {
            return EconomyResult.Fail(EconomyError.InsufficientItems);
        }

        if (!TryMultiply(unit, quantity, out long total))
        {
            return EconomyResult.Fail(EconomyError.InvalidAmount);
        }

        EconomyResult paid = wallet.TryCredit(CurrencyKind.Gold, total, CurrencyReason.VendorSale, tick);
        if (paid.IsOk)
        {
            _ = inventory.TryRemove(itemId, quantity);
        }

        return paid;
    }

    /// <summary>
    /// Sells <paramref name="quantity"/> units from one specific slot (so equipped or chosen stacks are never touched implicitly).
    /// </summary>
    /// <param name="wallet">Seller wallet.</param>
    /// <param name="inventory">Seller inventory.</param>
    /// <param name="slotIndex">Slot to sell from.</param>
    /// <param name="quantity">Positive quantity, at most the slot's quantity.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <returns>The outcome; on failure nothing changed.</returns>
    public EconomyResult TrySellSlot(Wallet wallet, InventoryContainer inventory, int slotIndex, int quantity, ulong tick = 0)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(inventory);
        if (quantity <= 0 || (uint)slotIndex >= (uint)inventory.Capacity)
        {
            return EconomyResult.Fail(EconomyError.InvalidAmount);
        }

        ItemSlot slot = inventory.GetSlot(slotIndex);
        if (slot.IsEmpty || slot.Quantity < quantity)
        {
            return EconomyResult.Fail(EconomyError.InsufficientItems);
        }

        if (!this.Prices.Items.TryGet(slot.ItemId, out _))
        {
            return EconomyResult.Fail(EconomyError.UnknownItem);
        }

        long unit = this.Prices.GetSellPrice(slot.ItemId);
        if (unit <= 0)
        {
            return EconomyResult.Fail(EconomyError.NotForSale);
        }

        if (!TryMultiply(unit, quantity, out long total))
        {
            return EconomyResult.Fail(EconomyError.InvalidAmount);
        }

        EconomyResult paid = wallet.TryCredit(CurrencyKind.Gold, total, CurrencyReason.VendorSale, tick);
        if (paid.IsOk)
        {
            int left = slot.Quantity - quantity;
            inventory.SetSlot(slotIndex, left > 0 ? slot with { Quantity = left } : ItemSlot.Empty);
        }

        return paid;
    }

    /// <summary>
    /// Repairs the item in <paramref name="slotIndex"/> to full durability for gold.
    /// </summary>
    /// <param name="wallet">Owner wallet.</param>
    /// <param name="inventory">Inventory holding the item.</param>
    /// <param name="slotIndex">Slot index.</param>
    /// <param name="tick">Simulation tick.</param>
    /// <returns>The outcome; on failure nothing changed.</returns>
    public EconomyResult TryRepair(Wallet wallet, InventoryContainer inventory, int slotIndex, ulong tick = 0)
    {
        ArgumentNullException.ThrowIfNull(wallet);
        ArgumentNullException.ThrowIfNull(inventory);
        if ((uint)slotIndex >= (uint)inventory.Capacity)
        {
            return EconomyResult.Fail(EconomyError.InvalidAmount);
        }

        ItemSlot slot = inventory.GetSlot(slotIndex);
        if (slot.IsEmpty || !this.Prices.Items.TryGet(slot.ItemId, out ItemCatalogEntry? item) || item.MaxDurability <= 0f)
        {
            return EconomyResult.Fail(EconomyError.NotRepairable);
        }

        float maxDurability = ItemEnhancementPolicy.GetEffectiveMaxDurability(item.MaxDurability, ItemEnhancementPolicy.GetUpgradeLevel(in slot));
        long cost = this.Prices.GetRepairCost(slot);
        if (cost <= 0 && slot.Durability >= maxDurability)
        {
            return EconomyResult.Fail(EconomyError.AlreadyRepaired);
        }

        EconomyResult paid = wallet.TrySpend(CurrencyKind.Gold, cost, CurrencyReason.Repair, tick);
        if (paid.IsOk)
        {
            inventory.SetSlot(slotIndex, slot with { Durability = maxDurability });
        }

        return paid;
    }

    private static bool TryMultiply(long unit, int quantity, out long total)
    {
        try
        {
            total = checked(unit * quantity);
            return true;
        }
        catch (OverflowException)
        {
            total = 0;
            return false;
        }
    }
}
