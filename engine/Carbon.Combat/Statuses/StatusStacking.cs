// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Statuses;

/// <summary>How a status effect reacts when it is applied again while active (ADR-029 D3).</summary>
public enum StatusStacking : byte
{
    /// <summary>
    /// One stack; the same or a higher level resets the duration and replaces the values (a higher level upgrades); a
    /// lower level is ignored (ADR-036 D3).
    /// </summary>
    Refresh = 0,

    /// <summary>
    /// One more stack up to the cap; the duration resets to the longer of the two; the entry keeps the higher level and its
    /// values (ADR-036 D3).
    /// </summary>
    Stack = 1,

    /// <summary>
    /// A higher level replaces the active one and a lower one is ignored (ADR-036 D3); at the same level a stronger
    /// application replaces it, an equal one extends it and a weaker one is ignored.
    /// </summary>
    Strongest = 2,
}
