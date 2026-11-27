// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Survival.Components;

namespace Carbon.Survival.Stamina;

/// <summary>Stamina regen multipliers per rest band (ADR-018), <c>stamina.json</c> <c>fatigueRegenScale</c>.</summary>
public sealed class StaminaFatigueScales
{
    /// <summary>Gets the multiplier while <see cref="RestState.Tired"/>.</summary>
    public float Tired { get; init; } = 1f;

    /// <summary>Gets the multiplier while <see cref="RestState.VeryTired"/>.</summary>
    public float VeryTired { get; init; } = 1f;

    /// <summary>Gets the multiplier while <see cref="RestState.Exhausted"/>.</summary>
    public float Exhausted { get; init; } = 1f;

    /// <summary>Gets the multiplier for <paramref name="state"/> (1 when rested).</summary>
    /// <param name="state">Rest band.</param>
    /// <returns>The regen multiplier.</returns>
    public float For(RestState state) => state switch
    {
        RestState.Tired => this.Tired,
        RestState.VeryTired => this.VeryTired,
        RestState.Exhausted => this.Exhausted,
        RestState.Rested => 1f,
        _ => 1f,
    };

    internal void Validate()
    {
        StaminaConfig.RequireRange(this.Tired, 0f, 1f, "fatigueRegenScale.tired");
        StaminaConfig.RequireRange(this.VeryTired, 0f, this.Tired, "fatigueRegenScale.veryTired");
        StaminaConfig.RequireRange(this.Exhausted, 0f, this.VeryTired, "fatigueRegenScale.exhausted");
    }
}
