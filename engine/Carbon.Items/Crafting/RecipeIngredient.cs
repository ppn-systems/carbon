// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Crafting;

/// <summary>
/// Required input item and quantity for a <see cref="CraftingRecipe"/>.
/// </summary>
/// <param name="ItemId">Required item definition identifier.</param>
/// <param name="Quantity">Required quantity.</param>
public readonly record struct RecipeIngredient(uint ItemId, int Quantity);
