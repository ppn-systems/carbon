// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Common;

/// <summary>
/// What killed a player (ADR-031), sent in <c>PlayerDeath</c> so the client can name it from its own localization.
/// Wire values are stable: append new members, never renumber.
/// </summary>
public enum DeathCause : byte
{
    /// <summary>Unknown or unattributed.</summary>
    Unknown = 0,

    /// <summary>A monster; <c>KillerArchetypeId</c> names its <c>monsters.json</c> archetype.</summary>
    Monster = 1,

    /// <summary>Starvation or malnutrition (ADR-015).</summary>
    Starvation = 2,
}
