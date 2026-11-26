// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Food;

/// <summary>
/// Nutrition and side effects of one edible item, keyed by the item id from <c>items.json</c>.
/// Loaded from <c>resources/config/food.json</c>.
/// </summary>
public sealed class FoodDefinition
{
    /// <summary>Gets the item id this entry applies to (e.g. <c>cooked_meat</c>).</summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>Gets the food points restored, in the same units as <see cref="Config.NeedsConfig.FoodMax"/>.</summary>
    public float Nutrition { get; init; }

    /// <summary>Gets the preparation quality.</summary>
    public FoodQuality Quality { get; init; }

    /// <summary>Gets the chance (0..1) that finishing this food causes food poisoning.</summary>
    public float PoisonChance { get; init; }

    /// <summary>Gets the mood offset of the memory left by eating this food (negative for raw food).</summary>
    public float MoodOffset { get; init; }

    /// <summary>Gets the seconds the mood memory lasts.</summary>
    public float MoodDurationSeconds { get; init; }

    /// <summary>Gets the seconds it takes to eat this food.</summary>
    public float EatSeconds { get; init; }

    /// <summary>Gets the online seconds until a stack of this food spoils; <c>0</c> means it never spoils.</summary>
    public float SpoilSeconds { get; init; }

    /// <summary>
    /// Gets the item id a spoiled stack turns into (e.g. <c>rotten_food</c>), or empty to remove the stack.
    /// </summary>
    public string SpoilsInto { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether this food spoils.</summary>
    public bool IsPerishable => this.SpoilSeconds > 0f;
}
