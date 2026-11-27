// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using Carbon.Survival.Stamina;

namespace Carbon.Host.Tests;

/// <summary>Stamina tuning with round numbers for ADR-033 behaviour tests (not the shipped <c>stamina.json</c>).</summary>
internal static class StaminaTestConfig
{
    public const float BareHandCost = 3f;
    public const float MeleeCost = 7f;
    public const float ToolCost = 9f;
    public const float RangedCost = 5f;
    public const float WoodAxeCost = 11f;
    public const float SprintDrainPerSecond = 15f;
    public const float BuildWorkPerSecond = 6f;
    public const float RegenStill = 6f;
    public const float RegenMoving = 2f;
    public const float RegenDelaySeconds = 1.5f;
    public const float ExhaustedRecoverFraction = 0.5f;
    public const float TiredScale = 0.5f;
    public const float StarvingScale = 0.2f;

    public static StaminaConfig Create() => new()
    {
        SprintDrainPerSecond = SprintDrainPerSecond,
        RegenDelaySeconds = RegenDelaySeconds,
        RegenPerSecondStill = RegenStill,
        RegenPerSecondMoving = RegenMoving,
        RestingRegenMultiplier = 2f,
        ExhaustedRecoverFraction = ExhaustedRecoverFraction,
        MinimumRegenScale = 0.1f,
        FatigueRegenScale = new() { Tired = TiredScale, VeryTired = 0.25f, Exhausted = 0.2f },
        HungerRegenScale = new() { Hungry = 0.5f, UrgentlyHungry = 0.25f, Starving = StarvingScale },
        SwingCosts = new() { BareHand = BareHandCost, Melee = MeleeCost, Tool = ToolCost, Ranged = RangedCost, Spell = 0f },
        ItemSwingCosts = new Dictionary<string, float> { ["wood_axe"] = WoodAxeCost },
        BuildWorkPerSecond = BuildWorkPerSecond,
    };
}
