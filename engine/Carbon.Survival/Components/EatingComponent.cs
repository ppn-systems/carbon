// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Components;

/// <summary>
/// Present while an entity is eating. Added by <see cref="Systems.EatActionSystem.BeginEat"/> and removed
/// when the meal completes or is interrupted.
/// </summary>
/// <param name="FoodIndex">Index of the food in <see cref="Food.FoodCatalog"/>.</param>
/// <param name="RemainingTicks">Fixed ticks left before the nutrition is applied.</param>
/// <param name="TotalTicks">Total fixed ticks the meal takes.</param>
/// <param name="LastHealth">Health seen on the previous tick; a drop means the eater took damage.</param>
public readonly record struct EatingComponent(int FoodIndex, int RemainingTicks, int TotalTicks, float LastHealth)
{
    /// <summary>Gets the meal progress in 0..1 for a HUD progress bar.</summary>
    public float Progress => this.TotalTicks > 0 ? 1f - ((float)this.RemainingTicks / this.TotalTicks) : 1f;
}
