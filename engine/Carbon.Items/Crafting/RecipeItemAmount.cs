// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Crafting;

/// <summary>
/// JSON shape of an item code and quantity inside a recipe.
/// </summary>
public sealed record RecipeItemAmount
{
    /// <summary>Gets the item code.</summary>
    public string Item { get; set; } = string.Empty;

    /// <summary>Gets the quantity.</summary>
    public int Qty { get; set; } = 1;
}
