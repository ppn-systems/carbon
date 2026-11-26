// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Components;

/// <summary>
/// RimWorld-style food thresholds, ordered from best to worst.
/// </summary>
public enum HungerState : byte
{
    /// <summary>Food above the hungry threshold; no penalties.</summary>
    Fed = 0,

    /// <summary>Food below the hungry threshold; minor mood penalty.</summary>
    Hungry = 1,

    /// <summary>Food below the urgent threshold; mood penalty, movement/attack slowdown, no health regen.</summary>
    UrgentlyHungry = 2,

    /// <summary>Food is empty; malnutrition rises and periodic damage is applied.</summary>
    Starving = 3,
}
