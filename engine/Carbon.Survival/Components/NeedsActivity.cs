// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Components;

/// <summary>
/// Activity the simulation reports for an entity each tick; scales how fast food and rest drain.
/// </summary>
public enum NeedsActivity : byte
{
    /// <summary>Standing still or idling in town.</summary>
    Idle = 0,

    /// <summary>Normal walking movement (the reference rate, multiplier 1.0 by default).</summary>
    Walking = 1,

    /// <summary>Sprinting.</summary>
    Sprinting = 2,

    /// <summary>In combat: attacking or being attacked.</summary>
    Fighting = 3,

    /// <summary>Swimming through water tiles.</summary>
    Swimming = 4,

    /// <summary>Sitting anywhere; recovers rest slowly instead of draining it.</summary>
    Resting = 5,

    /// <summary>Sleeping in a bed; recovers rest fastest (more in a roofed room).</summary>
    Sleeping = 6,
}
