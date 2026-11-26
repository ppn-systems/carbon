// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Items.Crafting;

/// <summary>
/// Integer work-speed model for timed crafting. A recipe's <see cref="CraftingRecipe.WorkTicks"/> is measured at
/// baseline speed (<see cref="BaselineUnitsPerTick"/>); skill raises the units contributed per tick.
/// </summary>
public static class CraftingWork
{
    /// <summary>Work units contributed per tick at skill level 0.</summary>
    public const int BaselineUnitsPerTick = 100;

    /// <summary>Extra work units per tick per skill level.</summary>
    public const int UnitsPerSkillLevel = 4;

    /// <summary>Skill level beyond which work speed no longer increases (3x baseline).</summary>
    public const int SkillSpeedCap = 50;

    /// <summary>
    /// Gets the work units a crafter with <paramref name="skillLevel"/> contributes per tick.
    /// </summary>
    /// <param name="skillLevel">Crafting skill level.</param>
    /// <returns>Units per tick in <c>[100, 300]</c>.</returns>
    public static int UnitsPerTick(int skillLevel) =>
        BaselineUnitsPerTick + (UnitsPerSkillLevel * Math.Clamp(skillLevel, 0, SkillSpeedCap));

    /// <summary>
    /// Gets the total work units required by <paramref name="recipe"/>.
    /// </summary>
    /// <param name="recipe">Recipe.</param>
    /// <returns>Total work units.</returns>
    public static int RequiredUnits(CraftingRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return checked(recipe.WorkTicks * BaselineUnitsPerTick);
    }
}
