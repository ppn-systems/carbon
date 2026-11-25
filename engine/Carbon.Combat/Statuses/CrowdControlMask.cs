// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Combat.Statuses;

/// <summary>
/// Crowd-control state flags conferred by active status effects.
/// </summary>
[Flags]
public enum CrowdControlMask : byte
{
    /// <summary>No crowd-control restrictions.</summary>
    None = 0,

    /// <summary>Actor cannot move or initiate actions.</summary>
    Stunned = 1 << 0,

    /// <summary>Actor cannot move but may still perform non-movement actions.</summary>
    Rooted = 1 << 1,

    /// <summary>Actor cannot cast abilities or spells.</summary>
    Silenced = 1 << 2,

    /// <summary>Actor movement speed is reduced.</summary>
    Slowed = 1 << 3,

    /// <summary>Actor is immune to incoming damage.</summary>
    Invulnerable = 1 << 4
}
