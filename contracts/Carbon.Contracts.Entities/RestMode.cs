// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Entities;

/// <summary>
/// How a player is resting (ADR-017). Values are stable wire bytes: append, never renumber.
/// </summary>
public enum RestMode : byte
{
    /// <summary>Awake and not resting.</summary>
    None = 0,

    /// <summary>Sitting on the ground; recovers rest slowly.</summary>
    Sitting = 1,

    /// <summary>Sleeping in a bed outdoors.</summary>
    Sleeping = 2,

    /// <summary>Sleeping in a bed inside an enclosed, roofed room; recovers rest fastest.</summary>
    SleepingComfortable = 3,
}
