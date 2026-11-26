// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Items.Crafting;

/// <summary>
/// Root JSON document of <c>resources/config/recipes.json</c>.
/// </summary>
public sealed record RecipeCatalogDocument
{
    /// <summary>Gets the schema version.</summary>
    public int Version { get; set; }

    /// <summary>Gets every recipe definition.</summary>
    public IReadOnlyList<RecipeDefinition> Recipes { get; set; } = [];
}
