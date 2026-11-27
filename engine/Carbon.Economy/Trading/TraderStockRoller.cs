// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Economy.Vendor;
using Carbon.Primitives.Random;

namespace Carbon.Economy.Trading;

/// <summary>One rolled stock slot: the item, how many units the epoch stocks and its price offset.</summary>
/// <param name="ItemId">Item id.</param>
/// <param name="Quantity">Units stocked.</param>
/// <param name="PriceOffsetPercent">Offset from the list price, within ± the profile variance.</param>
public readonly record struct TraderStockSlot(uint ItemId, int Quantity, int PriceOffsetPercent);

/// <summary>
/// Rolls a trader's stock for one restock epoch: a pure function of (world seed, trader key, epoch, profile, style),
/// so the same epoch always yields the same stock and audits can replay it. Zero-allocation.
/// </summary>
public static class TraderStockRoller
{
    /// <summary>Rolls the stock of an epoch.</summary>
    /// <param name="profile">Trader profile.</param>
    /// <param name="worldSeed">World seed.</param>
    /// <param name="traderKey">Trader key.</param>
    /// <param name="epoch">Restock epoch.</param>
    /// <param name="style">Village style index.</param>
    /// <param name="destination">Receives at most <see cref="TraderProfile.Slots"/> slots.</param>
    /// <returns>Number of slots written (fewer when the style has fewer eligible entries).</returns>
    public static int Roll(TraderProfile profile, ulong worldSeed, ulong traderKey, long epoch, byte style, Span<TraderStockSlot> destination)
    {
        ArgumentNullException.ThrowIfNull(profile);
        int entryCount = profile.Entries.Count;
        Span<bool> used = entryCount <= 64 ? stackalloc bool[64] : new bool[entryCount];
        used = used[..entryCount];
        used.Clear();
        int total = 0;
        for (int i = 0; i < entryCount; i++)
        {
            if (profile.Entries[i].AppliesTo(style))
            {
                total += profile.Entries[i].Weight;
            }
            else
            {
                used[i] = true;
            }
        }

        DeterministicRandom random = new(TraderClock.Mix(worldSeed ^ TraderClock.Mix(traderKey ^ TraderClock.Mix((ulong)epoch))));
        int count = 0;
        int want = Math.Min(profile.Slots, destination.Length);
        while (count < want && total > 0)
        {
            int roll = random.NextInt(0, total);
            for (int i = 0; i < entryCount; i++)
            {
                if (used[i])
                {
                    continue;
                }

                TraderStockEntry entry = profile.Entries[i];
                roll -= entry.Weight;
                if (roll < 0)
                {
                    used[i] = true;
                    total -= entry.Weight;
                    int quantity = random.NextInt(entry.MinQuantity, entry.MaxQuantity + 1);
                    int offset = random.NextInt(-profile.PriceVariancePercent, profile.PriceVariancePercent + 1);
                    destination[count++] = new TraderStockSlot(entry.ItemId, quantity, offset);
                    break;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Gets the unit price of a rolled slot for a buyer: the list price (base × buy markup, consumables by the buyer's
    /// level band) shifted by the slot offset, never at or below the vendor's buy-back price.
    /// </summary>
    public static long UnitPrice(PriceBook prices, in TraderStockSlot slot, int buyerLevel)
    {
        ArgumentNullException.ThrowIfNull(prices);
        long list = prices.GetListPrice(slot.ItemId, buyerLevel);
        long price = (long)Math.Ceiling(list * (100 + slot.PriceOffsetPercent) / 100.0);
        return Math.Max(Math.Max(1, price), prices.GetSellPrice(slot.ItemId) + 1);
    }
}
