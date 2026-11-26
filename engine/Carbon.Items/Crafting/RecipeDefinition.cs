// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;

namespace Carbon.Items.Crafting;

/// <summary>
/// JSON shape of one recipe in <c>recipes.json</c>. Items are referenced by code and resolved at load time.
/// </summary>
public sealed record RecipeDefinition
{
    /// <summary>Gets the stable numeric recipe id used on the wire.</summary>
    public uint Id { get; set; }

    /// <summary>Gets the stable recipe code.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets the required station.</summary>
    public CraftingStation Station { get; set; }

    /// <summary>Gets the work amount in ticks at baseline speed.</summary>
    public int WorkTicks { get; set; }

    /// <summary>Gets the minimum character level.</summary>
    public int UnlockLevel { get; set; }

    /// <summary>Gets the gating skill.</summary>
    public CraftingSkill Skill { get; set; }

    /// <summary>Gets the minimum skill level.</summary>
    public int SkillLevel { get; set; }

    /// <summary>Gets the consumed ingredients.</summary>
    public IReadOnlyList<RecipeItemAmount> Ingredients { get; set; } = [];

    /// <summary>Gets the produced outputs; the first entry is the primary output.</summary>
    public IReadOnlyList<RecipeItemAmount> Outputs { get; set; } = [];
}
