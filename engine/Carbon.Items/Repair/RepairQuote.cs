// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Items.Crafting;

namespace Carbon.Items.Repair;

/// <summary>
/// What a repair of one item costs and restores (ADR-028), computed by <see cref="RepairTable.TryQuote"/> from the
/// synced <c>repair.json</c>. The server spends exactly this; the client shows exactly this. Allocation-free.
/// </summary>
public readonly struct RepairQuote
{
    private readonly RepairFamily? _family;
    private readonly int _materialScale;
    private readonly int _materialDivisor;

    internal RepairQuote(
        RepairFamily family,
        uint itemId,
        RepairVenue venue,
        float durability,
        float maxDurability,
        int stepsNeeded,
        int steps,
        float restored,
        long goldFee,
        int materialScale,
        int materialDivisor)
    {
        _family = family;
        _materialScale = materialScale;
        _materialDivisor = materialDivisor;
        this.ItemId = itemId;
        this.Venue = venue;
        this.Durability = durability;
        this.MaxDurability = maxDurability;
        this.StepsNeeded = stepsNeeded;
        this.Steps = steps;
        this.RestoredDurability = restored;
        this.GoldFee = goldFee;
    }

    /// <summary>Gets the item being repaired.</summary>
    public uint ItemId { get; }

    /// <summary>Gets the venue the quote is for.</summary>
    public RepairVenue Venue { get; }

    /// <summary>Gets the durability before the repair.</summary>
    public float Durability { get; }

    /// <summary>Gets the item's maximum durability (enhancement included).</summary>
    public float MaxDurability { get; }

    /// <summary>Gets the steps a full repair takes.</summary>
    public int StepsNeeded { get; }

    /// <summary>Gets the steps this quote buys.</summary>
    public int Steps { get; }

    /// <summary>Gets whether this quote restores the item to full.</summary>
    public bool IsFull => this.Steps >= this.StepsNeeded;

    /// <summary>Gets the durability the repair adds.</summary>
    public float RestoredDurability { get; }

    /// <summary>Gets the durability after the repair.</summary>
    public float NewDurability => Math.Min(this.MaxDurability, this.Durability + this.RestoredDurability);

    /// <summary>Gets the gold fee (0 at a player station).</summary>
    public long GoldFee { get; }

    /// <summary>Gets the lowest station tier that can do the repair.</summary>
    public int RequiredStationTier => _family?.StationTier ?? 0;

    /// <summary>Gets the repair family id.</summary>
    public string FamilyId => _family?.Id ?? string.Empty;

    /// <summary>Gets how many different materials the repair needs.</summary>
    public int MaterialCount => _family is null ? 0 : _family.Materials.Length;

    /// <summary>Gets material <paramref name="index"/> and the quantity this quote spends.</summary>
    /// <param name="index">Material index, <c>0..</c><see cref="MaterialCount"/>.</param>
    /// <returns>The item id and quantity.</returns>
    public RecipeIngredient GetMaterial(int index)
    {
        if (_family is null || (uint)index >= (uint)_family.Materials.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        RecipeIngredient perFull = _family.Materials[index];
        long scaled = (long)perFull.Quantity * _materialScale;
        int quantity = (int)((scaled + _materialDivisor - 1) / _materialDivisor);
        return new RecipeIngredient(perFull.ItemId, quantity);
    }
}
