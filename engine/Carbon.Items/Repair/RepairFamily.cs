// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Crafting;

namespace Carbon.Items.Repair;

/// <summary>A compiled repair family (ADR-028): the materials of a full repair and the station tier it needs.</summary>
public sealed class RepairFamily
{
    private readonly RecipeIngredient[] _materials;

    internal RepairFamily(string id, int stationTier, RecipeIngredient[] materials)
    {
        this.Id = id;
        this.StationTier = stationTier;
        _materials = materials;
    }

    /// <summary>Gets the family id.</summary>
    public string Id { get; }

    /// <summary>Gets the lowest station tier that can repair the family.</summary>
    public int StationTier { get; }

    /// <summary>Gets the materials of a full repair of a tier-100 % item.</summary>
    public ReadOnlySpan<RecipeIngredient> Materials => _materials;
}
