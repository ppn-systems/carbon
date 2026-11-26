// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Survival.Events;

/// <summary>
/// Emitted when a meal finishes. The server consumes one unit of the food item from the eater's inventory
/// on this event.
/// </summary>
/// <param name="Entity">Eater.</param>
/// <param name="FoodIndex">Index of the food in <see cref="Food.FoodCatalog"/>.</param>
/// <param name="NutritionGained">Food points actually added (capped at the maximum).</param>
/// <param name="Poisoned">Whether the meal caused food poisoning.</param>
public readonly record struct MealCompletedEvent(Entity Entity, int FoodIndex, float NutritionGained, bool Poisoned);
