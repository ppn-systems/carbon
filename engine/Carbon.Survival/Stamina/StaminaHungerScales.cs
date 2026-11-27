// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Survival.Components;

namespace Carbon.Survival.Stamina;

/// <summary>Stamina regen multipliers per hunger band (ADR-015), <c>stamina.json</c> <c>hungerRegenScale</c>.</summary>
public sealed class StaminaHungerScales
{
    /// <summary>Gets the multiplier while <see cref="HungerState.Hungry"/>.</summary>
    public float Hungry { get; init; } = 1f;

    /// <summary>Gets the multiplier while <see cref="HungerState.UrgentlyHungry"/>.</summary>
    public float UrgentlyHungry { get; init; } = 1f;

    /// <summary>Gets the multiplier while <see cref="HungerState.Starving"/>.</summary>
    public float Starving { get; init; } = 1f;

    /// <summary>Gets the multiplier for <paramref name="state"/> (1 when fed).</summary>
    /// <param name="state">Hunger band.</param>
    /// <returns>The regen multiplier.</returns>
    public float For(HungerState state) => state switch
    {
        HungerState.Hungry => this.Hungry,
        HungerState.UrgentlyHungry => this.UrgentlyHungry,
        HungerState.Starving => this.Starving,
        HungerState.Fed => 1f,
        _ => 1f,
    };

    internal void Validate()
    {
        StaminaConfig.RequireRange(this.Hungry, 0f, 1f, "hungerRegenScale.hungry");
        StaminaConfig.RequireRange(this.UrgentlyHungry, 0f, this.Hungry, "hungerRegenScale.urgentlyHungry");
        StaminaConfig.RequireRange(this.Starving, 0f, this.UrgentlyHungry, "hungerRegenScale.starving");
    }
}
