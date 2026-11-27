// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Common;

/// <summary>
/// Where a player asks to repair an item (ADR-028). Wire values are stable: append new members, never renumber.
/// </summary>
public enum RepairVenue : byte
{
    /// <summary>No venue (an invalid request).</summary>
    None = 0,

    /// <summary>A player station in reach: a forge anvil, else a workbench. Materials only, no gold fee.</summary>
    Station = 1,

    /// <summary>A blacksmith NPC in reach. Materials plus the blacksmith's gold fee, and the highest station tier.</summary>
    Blacksmith = 2,
}
