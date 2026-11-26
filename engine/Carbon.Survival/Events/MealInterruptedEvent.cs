// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Survival.Events;

/// <summary>
/// Emitted when a meal stops before completing. No nutrition is applied and the item is not consumed.
/// </summary>
/// <param name="Entity">Eater.</param>
/// <param name="FoodIndex">Index of the food in <see cref="Food.FoodCatalog"/>.</param>
/// <param name="Reason">Why the meal stopped.</param>
public readonly record struct MealInterruptedEvent(Entity Entity, int FoodIndex, MealInterruptReason Reason);
