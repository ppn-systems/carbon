// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Crafting;

namespace Carbon.Items.Components;

/// <summary>
/// Timed craft in progress on a crafter or station entity. Ingredients are consumed when the job starts
/// (<see cref="RecipeMatcher.TryConsumeIngredients"/>) and outputs are delivered on completion
/// (<see cref="RecipeMatcher.TryDeliverOutputs"/>), so no item is ever duplicated or lost.
/// Unmanaged struct component advanced by <see cref="Systems.CraftingJobSystem"/>.
/// </summary>
/// <param name="RecipeId">Recipe being crafted.</param>
/// <param name="Station">Station the job runs at.</param>
/// <param name="WorkRequired">Total work units required.</param>
/// <param name="WorkPerTick">Work units contributed per tick (skill-scaled).</param>
/// <param name="WorkDone">Work units accumulated so far.</param>
/// <param name="State">Lifecycle state.</param>
public readonly record struct CraftingJobComponent(
    uint RecipeId,
    CraftingStation Station,
    int WorkRequired,
    int WorkPerTick,
    int WorkDone = 0,
    CraftingJobState State = CraftingJobState.Running)
{
    /// <summary>Gets the completion fraction in <c>[0, 1]</c>.</summary>
    public float Progress => this.WorkRequired <= 0 ? 1f : Math.Min(1f, (float)this.WorkDone / this.WorkRequired);

    /// <summary>
    /// Creates a running job for <paramref name="recipe"/> crafted by someone with <paramref name="skillLevel"/>.
    /// </summary>
    /// <param name="recipe">Recipe to craft.</param>
    /// <param name="skillLevel">Crafter skill level for the recipe's skill.</param>
    /// <returns>The new job (already <see cref="CraftingJobState.Completed"/> for instant recipes).</returns>
    public static CraftingJobComponent Start(CraftingRecipe recipe, int skillLevel)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        int required = CraftingWork.RequiredUnits(recipe);
        return new CraftingJobComponent(
            recipe.Id,
            recipe.Station,
            required,
            CraftingWork.UnitsPerTick(skillLevel),
            State: required == 0 ? CraftingJobState.Completed : CraftingJobState.Running);
    }

    /// <summary>
    /// Advances the job by one tick.
    /// </summary>
    /// <returns>The advanced job; <see cref="CraftingJobState.Completed"/> once work reaches the requirement.</returns>
    public CraftingJobComponent Advance()
    {
        if (this.State != CraftingJobState.Running)
        {
            return this;
        }

        int done = (int)Math.Min((long)this.WorkDone + this.WorkPerTick, this.WorkRequired);
        return this with
        {
            WorkDone = done,
            State = done >= this.WorkRequired ? CraftingJobState.Completed : CraftingJobState.Running
        };
    }
}
