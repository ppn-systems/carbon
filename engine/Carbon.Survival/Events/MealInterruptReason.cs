// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Events;

/// <summary>
/// Why a meal ended before completing.
/// </summary>
public enum MealInterruptReason : byte
{
    /// <summary>The eater took damage.</summary>
    Damaged = 0,

    /// <summary>The server or player cancelled the meal.</summary>
    Cancelled = 1,

    /// <summary>The eater died or lost its needs component.</summary>
    Died = 2,
}
