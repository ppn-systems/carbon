// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Common;

/// <summary>
/// What happened to a worn item (ADR-035), sent in <c>ItemDurabilityAlert</c> so the client can show its own localized
/// toast and effect. Wire values are stable: append new members, never renumber.
/// </summary>
public enum DurabilityAlertKind : byte
{
    /// <summary>No alert (never sent).</summary>
    None = 0,

    /// <summary>The item just fell to the critical share of its durability (<c>repair.json → durabilityWarning</c>).</summary>
    AboutToBreak = 1,

    /// <summary>The item reached 0 durability and was destroyed; its slot is now empty.</summary>
    Broke = 2,
}
