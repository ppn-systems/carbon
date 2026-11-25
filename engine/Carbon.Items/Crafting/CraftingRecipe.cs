// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Data.Abstractions;
using Carbon.Items.Containers;

namespace Carbon.Items.Crafting;

/// <summary>
/// Static crafting recipe definition mapping required ingredients to an output <see cref="ItemSlot"/>.
/// </summary>
/// <param name="Id">Unique recipe identifier.</param>
/// <param name="Ingredients">Required input ingredients.</param>
/// <param name="Output">Produced output item entry.</param>
/// <param name="CraftSeconds">Craft duration in seconds (0 for instant).</param>
public sealed record CraftingRecipe(
    uint Id,
    IReadOnlyList<RecipeIngredient> Ingredients,
    ItemSlot Output,
    float CraftSeconds = 0f) : IDataDefinition<uint>;
