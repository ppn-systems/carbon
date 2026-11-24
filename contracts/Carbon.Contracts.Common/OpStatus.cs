// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Common;

/// <summary>
/// Canonical outcome status carried by <see cref="MutationResult"/> across all modules.
/// Values are stable and ascending so the wire format stays additive: append, never renumber.
/// </summary>
public enum OpStatus : byte
{
    /// <summary>The mutation succeeded.</summary>
    Ok = 0,

    /// <summary>The caller is not allowed to perform the action.</summary>
    Forbidden = 1,

    /// <summary>The target (tile, prop, structure) does not exist.</summary>
    NotFound = 2,

    /// <summary>The request was rejected because of invalid input.</summary>
    Invalid = 3,

    /// <summary>The target is occupied or in a conflicting state.</summary>
    Conflict = 4,

    /// <summary>The target is outside the caller's interaction range or area of interest.</summary>
    OutOfRange = 5,

    /// <summary>The caller sent too many requests; retry later.</summary>
    RateLimited = 6,
}
