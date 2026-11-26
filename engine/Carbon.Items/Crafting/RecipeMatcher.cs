// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;

namespace Carbon.Items.Crafting;

/// <summary>
/// Stateless, server-authoritative helper validating and executing <see cref="CraftingRecipe"/> crafts against an
/// <see cref="InventoryContainer"/>. Every mutating call stages the whole operation on an <see cref="InventoryDraft"/>
/// and commits only when all ingredients were removed and every output fit: crafting is all-or-nothing.
/// </summary>
public static class RecipeMatcher
{
    /// <summary>
    /// Checks whether <paramref name="inventory"/> holds all ingredients required by <paramref name="recipe"/>.
    /// </summary>
    /// <param name="inventory">Player or station inventory container.</param>
    /// <param name="recipe">Recipe to validate.</param>
    /// <returns><see langword="true"/> if all required quantities are present; otherwise, <see langword="false"/>.</returns>
    public static bool CanCraft(InventoryContainer inventory, CraftingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(recipe);

        Span<ItemSlot> buffer = inventory.Capacity <= InventoryDraft.StackBufferLimit
            ? stackalloc ItemSlot[InventoryDraft.StackBufferLimit]
            : new ItemSlot[inventory.Capacity];
        InventoryDraft draft = InventoryDraft.Begin(inventory, buffer);
        return ConsumeIngredients(draft, recipe) == CraftFailure.None;
    }

    /// <summary>
    /// Legacy instant craft using one stack size for every output. Prefer
    /// <see cref="TryCraft(InventoryContainer, CraftingRecipe, IStackSizeProvider, in CraftContext)"/>.
    /// </summary>
    /// <param name="inventory">Target inventory container.</param>
    /// <param name="recipe">Recipe to craft.</param>
    /// <param name="maxOutputStackSize">Maximum stack size for the crafted output items.</param>
    /// <returns><see langword="true"/> if crafted and output added; otherwise, <see langword="false"/>.</returns>
    public static bool TryCraft(InventoryContainer inventory, CraftingRecipe recipe, int maxOutputStackSize = 99)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputStackSize);
        return TryCraft(inventory, recipe, new FixedStackSizeProvider(maxOutputStackSize), CraftContext.Unrestricted).IsOk;
    }

    /// <summary>
    /// Validates station, level and skill requirements only.
    /// </summary>
    /// <param name="recipe">Recipe to check.</param>
    /// <param name="context">Crafter state.</param>
    /// <returns><see cref="CraftResult.Ok"/> or the first failing requirement.</returns>
    public static CraftResult CheckRequirements(CraftingRecipe recipe, in CraftContext context)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        if (!IsWellFormed(recipe))
        {
            return CraftResult.Fail(CraftFailure.InvalidRecipe);
        }

        if (!context.IgnoreStation && recipe.Station != CraftingStation.None && recipe.Station != context.Station)
        {
            return CraftResult.Fail(CraftFailure.WrongStation);
        }

        if (context.CharacterLevel < recipe.UnlockLevel)
        {
            return CraftResult.Fail(CraftFailure.LevelTooLow);
        }

        if (recipe.Skill != CraftingSkill.None && context.SkillLevel < recipe.SkillLevel)
        {
            return CraftResult.Fail(CraftFailure.SkillTooLow);
        }

        return CraftResult.Ok;
    }

    /// <summary>
    /// Dry-runs an instant craft without mutating <paramref name="inventory"/>.
    /// </summary>
    /// <param name="inventory">Crafter inventory.</param>
    /// <param name="recipe">Recipe to validate.</param>
    /// <param name="stacks">Stack size provider for outputs.</param>
    /// <param name="context">Crafter state.</param>
    /// <returns>The result the real craft would produce.</returns>
    public static CraftResult Validate(InventoryContainer inventory, CraftingRecipe recipe, IStackSizeProvider stacks, in CraftContext context) =>
        Run(inventory, recipe, stacks, context, consume: true, produce: true, commit: false);

    /// <summary>
    /// Atomically consumes every ingredient and adds every output. Nothing changes unless all of it succeeds.
    /// </summary>
    /// <param name="inventory">Crafter inventory.</param>
    /// <param name="recipe">Recipe to craft.</param>
    /// <param name="stacks">Stack size provider for outputs.</param>
    /// <param name="context">Crafter state.</param>
    /// <returns><see cref="CraftResult.Ok"/> or the rejection reason.</returns>
    public static CraftResult TryCraft(InventoryContainer inventory, CraftingRecipe recipe, IStackSizeProvider stacks, in CraftContext context) =>
        Run(inventory, recipe, stacks, context, consume: true, produce: true, commit: true);

    /// <summary>
    /// Starts a timed craft: atomically consumes the ingredients, provided the outputs would currently fit afterwards.
    /// Deliver the outputs with <see cref="TryDeliverOutputs"/> when the work completes.
    /// </summary>
    /// <param name="inventory">Crafter inventory.</param>
    /// <param name="recipe">Recipe to start.</param>
    /// <param name="stacks">Stack size provider for outputs.</param>
    /// <param name="context">Crafter state.</param>
    /// <returns><see cref="CraftResult.Ok"/> or the rejection reason.</returns>
    public static CraftResult TryConsumeIngredients(InventoryContainer inventory, CraftingRecipe recipe, IStackSizeProvider stacks, in CraftContext context)
    {
        CraftResult check = Run(inventory, recipe, stacks, context, consume: true, produce: true, commit: false);
        return check.IsOk
            ? Run(inventory, recipe, stacks, context, consume: true, produce: false, commit: true)
            : check;
    }

    /// <summary>
    /// Adds every output of <paramref name="recipe"/> or nothing at all.
    /// </summary>
    /// <param name="inventory">Receiving inventory.</param>
    /// <param name="recipe">Completed recipe.</param>
    /// <param name="stacks">Stack size provider for outputs.</param>
    /// <returns><see cref="CraftResult.Ok"/> or <see cref="CraftFailure.InventoryFull"/>.</returns>
    public static CraftResult TryDeliverOutputs(InventoryContainer inventory, CraftingRecipe recipe, IStackSizeProvider stacks) =>
        Run(inventory, recipe, stacks, CraftContext.Unrestricted, consume: false, produce: true, commit: true);

    private static CraftResult Run(
        InventoryContainer inventory,
        CraftingRecipe recipe,
        IStackSizeProvider stacks,
        in CraftContext context,
        bool consume,
        bool produce,
        bool commit)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(stacks);

        CraftResult requirements = CheckRequirements(recipe, context);
        if (!requirements.IsOk)
        {
            return requirements;
        }

        Span<ItemSlot> buffer = inventory.Capacity <= InventoryDraft.StackBufferLimit
            ? stackalloc ItemSlot[InventoryDraft.StackBufferLimit]
            : new ItemSlot[inventory.Capacity];
        InventoryDraft draft = InventoryDraft.Begin(inventory, buffer);

        if (consume)
        {
            CraftFailure failure = ConsumeIngredients(draft, recipe);
            if (failure != CraftFailure.None)
            {
                return CraftResult.Fail(failure);
            }
        }

        if (produce)
        {
            for (int i = 0; i < recipe.OutputCount; i++)
            {
                ItemSlot output = recipe.GetOutput(i);
                if (draft.Add(output, Math.Max(1, stacks.GetMaxStack(output.ItemId))) != 0)
                {
                    return CraftResult.Fail(CraftFailure.InventoryFull);
                }
            }
        }

        if (commit)
        {
            draft.CommitTo(inventory);
        }

        return CraftResult.Ok;
    }

    private static CraftFailure ConsumeIngredients(InventoryDraft draft, CraftingRecipe recipe)
    {
        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            RecipeIngredient ingredient = recipe.Ingredients[i];
            if (ingredient.Quantity > 0 && draft.Remove(ingredient.ItemId, ingredient.Quantity) != ingredient.Quantity)
            {
                return CraftFailure.MissingIngredients;
            }
        }

        return CraftFailure.None;
    }

    private static bool IsWellFormed(CraftingRecipe recipe)
    {
        if (recipe.Output.IsEmpty || recipe.WorkTicks < 0)
        {
            return false;
        }

        for (int i = 1; i < recipe.OutputCount; i++)
        {
            if (recipe.GetOutput(i).IsEmpty)
            {
                return false;
            }
        }

        return true;
    }
}
