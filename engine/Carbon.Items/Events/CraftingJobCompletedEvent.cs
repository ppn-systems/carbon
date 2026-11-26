// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.Items.Events;

/// <summary>
/// Published by <see cref="Systems.CraftingJobSystem"/> on the tick a crafting job finishes its work.
/// The server handler then delivers outputs via <see cref="Crafting.RecipeMatcher.TryDeliverOutputs"/>.
/// </summary>
/// <param name="Crafter">Entity owning the job.</param>
/// <param name="RecipeId">Completed recipe.</param>
public readonly record struct CraftingJobCompletedEvent(Entity Crafter, uint RecipeId);
