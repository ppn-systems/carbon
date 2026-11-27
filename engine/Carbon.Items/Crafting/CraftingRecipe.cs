// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Data.Abstractions;
using Carbon.Items.Containers;

namespace Carbon.Items.Crafting;

/// <summary>
/// Static crafting recipe definition mapping required ingredients to an output <see cref="ItemSlot"/>
/// plus optional byproducts, gated by station, character level and skill.
/// </summary>
/// <param name="Id">Unique recipe identifier.</param>
/// <param name="Ingredients">Required input ingredients.</param>
/// <param name="Output">Primary produced output item entry.</param>
/// <param name="Byproducts">Additional outputs produced alongside <paramref name="Output"/> (multi-output recipes).</param>
/// <param name="Station">Required crafting station.</param>
/// <param name="UnlockLevel">Minimum character level.</param>
/// <param name="Skill">Crafting skill the recipe is gated by.</param>
/// <param name="SkillLevel">Minimum <paramref name="Skill"/> level.</param>
/// <param name="Code">Stable symbolic recipe code.</param>
public sealed record CraftingRecipe(
    uint Id,
    IReadOnlyList<RecipeIngredient> Ingredients,
    ItemSlot Output,
    IReadOnlyList<ItemSlot>? Byproducts = null,
    CraftingStation Station = CraftingStation.None,
    int UnlockLevel = 0,
    CraftingSkill Skill = CraftingSkill.None,
    int SkillLevel = 0,
    string Code = "") : IDataDefinition<uint>
{
    /// <summary>Gets the number of outputs (primary plus byproducts).</summary>
    public int OutputCount => 1 + (this.Byproducts?.Count ?? 0);

    /// <summary>
    /// Gets output <paramref name="index"/> where <c>0</c> is <see cref="Output"/> and later indices are byproducts.
    /// </summary>
    /// <param name="index">Output index in <c>[0, OutputCount)</c>.</param>
    /// <returns>The output item entry.</returns>
    public ItemSlot GetOutput(int index) => index == 0 ? this.Output : this.Byproducts![index - 1];
}
