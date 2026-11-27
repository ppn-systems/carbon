// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Survival.Components;

namespace Carbon.Survival.Stamina;

/// <summary>What the body is doing while stamina regenerates (ADR-033).</summary>
/// <param name="IsMoving">Whether the player moved this tick.</param>
/// <param name="IsResting">Whether the player sits or sleeps (ADR-018).</param>
/// <param name="Fatigue">Rest band from the needs.</param>
/// <param name="Hunger">Hunger band from the needs.</param>
/// <param name="EffectScale">
/// Extra multiplier from status effects (ADR-029). <c>1</c> until that system feeds stamina modifiers in.
/// </param>
public readonly record struct StaminaRegenContext(
    bool IsMoving,
    bool IsResting,
    RestState Fatigue,
    HungerState Hunger,
    float EffectScale = 1f);
