// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Common;

/// <summary>
/// The result of one forge enhancement roll (ADR-020). The server rolls it; the client only displays it.
/// Wire values are stable: append new members, never renumber.
/// </summary>
public enum EnhancementOutcome : byte
{
    /// <summary>No roll happened (the attempt was rejected before anything was spent).</summary>
    None = 0,

    /// <summary>The item gained one level.</summary>
    Success = 1,

    /// <summary>The roll failed and the item kept its level.</summary>
    NoChange = 2,

    /// <summary>The roll failed and the item lost one level.</summary>
    Downgrade = 3,

    /// <summary>The roll failed and the item was destroyed.</summary>
    Destroyed = 4,
}
