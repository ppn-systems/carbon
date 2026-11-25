// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Items.Containers;

namespace Carbon.Items.Crafting;

/// <summary>
/// Stateless helper validating and executing <see cref="CraftingRecipe"/> consumption against an <see cref="InventoryContainer"/>.
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

        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            RecipeIngredient ingredient = recipe.Ingredients[i];
            if (ingredient.Quantity <= 0)
            {
                continue;
            }

            if (inventory.CountItem(ingredient.ItemId) < ingredient.Quantity)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Attempts to consume ingredients from <paramref name="inventory"/> and insert <see cref="CraftingRecipe.Output"/>.
    /// Does not consume ingredients if output cannot fit completely.
    /// </summary>
    /// <param name="inventory">Target inventory container.</param>
    /// <param name="recipe">Recipe to craft.</param>
    /// <param name="maxOutputStackSize">Maximum stack size for the crafted output item.</param>
    /// <returns><see langword="true"/> if crafted and output added; otherwise, <see langword="false"/>.</returns>
    public static bool TryCraft(InventoryContainer inventory, CraftingRecipe recipe, int maxOutputStackSize = 99)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputStackSize);

        if (!CanCraft(inventory, recipe))
        {
            return false;
        }

        if (!CanFitOutput(inventory, recipe, maxOutputStackSize))
        {
            return false;
        }

        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            RecipeIngredient ingredient = recipe.Ingredients[i];
            if (ingredient.Quantity > 0)
            {
                _ = inventory.TryRemove(ingredient.ItemId, ingredient.Quantity);
            }
        }

        _ = inventory.TryAdd(recipe.Output, maxOutputStackSize);
        return true;
    }

    private static bool CanFitOutput(InventoryContainer inventory, CraftingRecipe recipe, int maxOutputStackSize)
    {
        ItemSlot output = recipe.Output;
        if (output.IsEmpty)
        {
            return true;
        }

        int freeCapacity = 0;
        ReadOnlySpan<ItemSlot> slots = inventory.Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            ItemSlot slot = slots[i];
            if (slot.IsEmpty)
            {
                freeCapacity += output.IsStackable ? maxOutputStackSize : 1;
            }
            else if (output.IsStackable && slot.IsStackable && slot.ItemId == output.ItemId && slot.Quantity < maxOutputStackSize)
            {
                freeCapacity += maxOutputStackSize - slot.Quantity;
            }
        }

        if (freeCapacity >= output.Quantity)
        {
            return true;
        }

        // Account for slots that will become empty once ingredients are consumed.
        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            RecipeIngredient ingredient = recipe.Ingredients[i];
            if (ingredient.Quantity <= 0)
            {
                continue;
            }

            int remainingToConsume = ingredient.Quantity;
            for (int s = 0; s < slots.Length && remainingToConsume > 0; s++)
            {
                ItemSlot slot = slots[s];
                if (slot.ItemId != ingredient.ItemId || slot.Quantity <= 0)
                {
                    continue;
                }

                if (remainingToConsume >= slot.Quantity)
                {
                    freeCapacity += output.IsStackable ? maxOutputStackSize : 1;
                    remainingToConsume -= slot.Quantity;
                }
                else
                {
                    remainingToConsume = 0;
                }
            }
        }

        return freeCapacity >= output.Quantity;
    }
}
