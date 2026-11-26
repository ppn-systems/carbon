// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Crafting;

/// <summary>
/// Crafter state used to validate recipe requirements.
/// </summary>
/// <param name="Station">Station the crafter is currently using (<see cref="CraftingStation.None"/> when hand-crafting).</param>
/// <param name="CharacterLevel">Character level.</param>
/// <param name="SkillLevel">Level of the recipe's <see cref="CraftingRecipe.Skill"/>.</param>
public readonly record struct CraftContext(CraftingStation Station, int CharacterLevel, int SkillLevel)
{
    /// <summary>Gets a context that satisfies every requirement (tests, admin tools, legacy callers).</summary>
    public static CraftContext Unrestricted => new(CraftingStation.None, int.MaxValue, int.MaxValue) { IgnoreStation = true };

    /// <summary>Gets a value indicating whether station checks are skipped.</summary>
    public bool IgnoreStation { get; init; }
}
