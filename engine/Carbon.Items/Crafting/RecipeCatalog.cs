// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using Carbon.Data.Registry;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;

namespace Carbon.Items.Crafting;

/// <summary>
/// Validated recipe registry compiled from <c>recipes.json</c> against an <see cref="ItemCatalog"/>.
/// Every item code is resolved at load time, so a recipe can never reference a missing item at runtime.
/// </summary>
public sealed class RecipeCatalog
{
    private readonly DefinitionRegistry<uint, CraftingRecipe> _byId;
    private readonly FrozenDictionary<string, CraftingRecipe> _byCode;

    private RecipeCatalog(DefinitionRegistry<uint, CraftingRecipe> byId, FrozenDictionary<string, CraftingRecipe> byCode)
    {
        _byId = byId;
        _byCode = byCode;
    }

    /// <summary>Gets every recipe in file order.</summary>
    public ReadOnlySpan<CraftingRecipe> All => _byId.All;

    /// <summary>Gets the number of recipes.</summary>
    public int Count => _byId.Count;

    /// <summary>Gets the recipes as a registry keyed by recipe id.</summary>
    public DefinitionRegistry<uint, CraftingRecipe> Registry => _byId;

    /// <summary>
    /// Parses, validates and compiles a <c>recipes.json</c> document.
    /// </summary>
    /// <param name="json">JSON document text.</param>
    /// <param name="items">Item catalog used to resolve item codes.</param>
    /// <returns>The compiled catalog.</returns>
    /// <exception cref="InvalidDataException">Thrown when the document is malformed or references unknown items.</exception>
    public static RecipeCatalog LoadFromJson(string json, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(items);

        RecipeCatalogDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, ItemsJsonContext.Default.RecipeCatalogDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"recipes.json is malformed: {ex.Message}", ex);
        }

        return Compile(document?.Recipes ?? throw new InvalidDataException("recipes.json is empty."), items);
    }

    /// <summary>
    /// Compiles recipe definitions against <paramref name="items"/>.
    /// </summary>
    /// <param name="definitions">Recipe definitions.</param>
    /// <param name="items">Item catalog used to resolve item codes.</param>
    /// <returns>The compiled catalog.</returns>
    /// <exception cref="InvalidDataException">Thrown when a definition is invalid.</exception>
    public static RecipeCatalog Compile(IReadOnlyList<RecipeDefinition> definitions, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(items);

        List<CraftingRecipe> recipes = new(definitions.Count);
        Dictionary<string, CraftingRecipe> byCode = new(StringComparer.Ordinal);
        for (int i = 0; i < definitions.Count; i++)
        {
            RecipeDefinition definition = definitions[i];
            if (definition.Id == 0 || string.IsNullOrWhiteSpace(definition.Code))
            {
                throw new InvalidDataException($"Recipe #{i} must have a non-zero id and a code.");
            }

            if (definition.WorkTicks < 0 || definition.UnlockLevel < 0 || definition.SkillLevel < 0)
            {
                throw new InvalidDataException($"Recipe '{definition.Code}' has negative workTicks/unlockLevel/skillLevel.");
            }

            if (definition.Ingredients.Count == 0 || definition.Outputs.Count == 0)
            {
                throw new InvalidDataException($"Recipe '{definition.Code}' needs at least one ingredient and one output.");
            }

            RecipeIngredient[] ingredients = new RecipeIngredient[definition.Ingredients.Count];
            for (int k = 0; k < ingredients.Length; k++)
            {
                RecipeItemAmount amount = definition.Ingredients[k];
                uint itemId = ResolvePositive(items, definition.Code, amount).Id;
                for (int j = 0; j < k; j++)
                {
                    if (ingredients[j].ItemId == itemId)
                    {
                        throw new InvalidDataException($"Recipe '{definition.Code}' lists ingredient '{amount.Item}' twice.");
                    }
                }

                ingredients[k] = new RecipeIngredient(itemId, amount.Qty);
            }

            ItemSlot[] outputs = new ItemSlot[definition.Outputs.Count];
            for (int k = 0; k < outputs.Length; k++)
            {
                RecipeItemAmount amount = definition.Outputs[k];
                ItemCatalogEntry item = ResolvePositive(items, definition.Code, amount);
                outputs[k] = new ItemSlot(item.Id, amount.Qty, Durability: item.MaxDurability);
            }

            CraftingRecipe recipe = new(
                definition.Id,
                ingredients,
                outputs[0],
                CraftSeconds: 0f,
                Byproducts: outputs.Length > 1 ? outputs[1..] : null,
                Station: definition.Station,
                WorkTicks: definition.WorkTicks,
                UnlockLevel: definition.UnlockLevel,
                Skill: definition.Skill,
                SkillLevel: definition.SkillLevel,
                Code: definition.Code);

            if (!byCode.TryAdd(definition.Code, recipe))
            {
                throw new InvalidDataException($"Duplicate recipe code '{definition.Code}'.");
            }

            recipes.Add(recipe);
        }

        DefinitionRegistry<uint, CraftingRecipe> registry;
        try
        {
            registry = new DefinitionRegistry<uint, CraftingRecipe>(recipes);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }

        return new RecipeCatalog(registry, byCode.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// Looks up a recipe by numeric id.
    /// </summary>
    /// <param name="id">Recipe id.</param>
    /// <param name="recipe">The recipe when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryGet(uint id, [MaybeNullWhen(false)] out CraftingRecipe recipe) => _byId.TryGet(id, out recipe);

    /// <summary>
    /// Looks up a recipe by code.
    /// </summary>
    /// <param name="code">Recipe code.</param>
    /// <param name="recipe">The recipe when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryGetByCode(string code, [MaybeNullWhen(false)] out CraftingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(code);
        return _byCode.TryGetValue(code, out recipe);
    }

    private static ItemCatalogEntry ResolvePositive(ItemCatalog items, string recipeCode, RecipeItemAmount amount)
    {
        if (amount.Qty <= 0)
        {
            throw new InvalidDataException($"Recipe '{recipeCode}' has non-positive quantity for '{amount.Item}'.");
        }

        return items.TryGetByCode(amount.Item, out ItemCatalogEntry? item)
            ? item
            : throw new InvalidDataException($"Recipe '{recipeCode}' references unknown item '{amount.Item}'.");
    }
}
