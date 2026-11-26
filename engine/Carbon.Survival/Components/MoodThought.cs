// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Components;

/// <summary>
/// Kind of timed mood memory left by an event, such as eating. One memory per kind is kept (refreshed on repeat).
/// </summary>
public enum MoodThought : byte
{
    /// <summary>Empty memory slot.</summary>
    None = 0,

    /// <summary>Ate raw food.</summary>
    AteRawFood = 1,

    /// <summary>Ate a simple cooked meal.</summary>
    AteSimpleMeal = 2,

    /// <summary>Ate a fine meal.</summary>
    AteFineMeal = 3,

    /// <summary>Ate a lavish meal.</summary>
    AteLavishMeal = 4,
}
