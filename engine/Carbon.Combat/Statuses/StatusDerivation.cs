// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Statuses;

/// <summary>Condition a derived status effect mirrors; derived effects have no timer (ADR-029 D2).</summary>
public enum StatusDerivation : byte
{
    /// <summary>A timed effect, applied and expired by the buffer.</summary>
    None = 0,

    /// <summary>Hungry or urgently hungry (stacks 1–2).</summary>
    Hungry = 1,

    /// <summary>Starving.</summary>
    Starving = 2,

    /// <summary>Tired, very tired or exhausted (stacks 1–3).</summary>
    Tired = 3,

    /// <summary>Food at or above the needs full threshold.</summary>
    WellFed = 4,
}
