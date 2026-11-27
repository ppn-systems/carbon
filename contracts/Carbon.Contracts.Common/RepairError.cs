// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Common;

/// <summary>
/// Why the server refused a repair (ADR-028). The client maps each value to a localized message.
/// Wire values are stable: append new members, never renumber.
/// </summary>
public enum RepairError : byte
{
    /// <summary>The repair ran.</summary>
    None = 0,

    /// <summary>The slot, step count or venue is not valid.</summary>
    InvalidRequest = 1,

    /// <summary>The slot is empty.</summary>
    EmptySlot = 2,

    /// <summary>The slot holds another item than the client named (it moved before the request ran).</summary>
    ItemMismatch = 3,

    /// <summary>The item has no durability or no repair family in <c>repair.json</c>.</summary>
    NotRepairable = 4,

    /// <summary>The item is already at full durability.</summary>
    NotDamaged = 5,

    /// <summary>More steps were asked than the item is missing.</summary>
    TooManySteps = 6,

    /// <summary>No station or blacksmith of the asked venue is in reach.</summary>
    OutOfRange = 7,

    /// <summary>The station in reach is below the tier the item needs.</summary>
    StationTierTooLow = 8,

    /// <summary>The bag does not hold every material the repair needs.</summary>
    MissingMaterials = 9,

    /// <summary>The wallet cannot pay the blacksmith's fee.</summary>
    NotEnoughGold = 10,
}
