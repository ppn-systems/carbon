// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Statuses;

/// <summary>Whether a status effect helps or hurts its owner.</summary>
public enum StatusCategory : byte
{
    /// <summary>A harmful effect.</summary>
    Debuff = 0,

    /// <summary>A helpful effect.</summary>
    Buff = 1,
}
