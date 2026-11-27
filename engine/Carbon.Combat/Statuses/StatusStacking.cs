// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Statuses;

/// <summary>How a status effect reacts when it is applied again while active (ADR-029 D3).</summary>
public enum StatusStacking : byte
{
    /// <summary>One stack; the duration resets and the magnitude is replaced.</summary>
    Refresh = 0,

    /// <summary>One more stack up to the cap; the duration resets to the longer of the two.</summary>
    Stack = 1,

    /// <summary>Only a stronger application replaces the active one; an equal one extends it; a weaker one is ignored.</summary>
    Strongest = 2,
}
