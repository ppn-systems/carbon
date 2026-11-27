// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Combat.Statuses;

/// <summary>What applied a status effect, sent to the owner for the details panel (ADR-029 D9).</summary>
public enum StatusSource : byte
{
    /// <summary>Unknown or not applicable.</summary>
    None = 0,

    /// <summary>A monster attack; the source id is the monster archetype id.</summary>
    Monster = 1,

    /// <summary>Food (food poisoning).</summary>
    Food = 2,

    /// <summary>Survival needs (hunger, rest), for derived effects.</summary>
    Needs = 3,

    /// <summary>A heavy hit; the source id is the monster archetype id when a monster dealt it.</summary>
    HeavyHit = 4,

    /// <summary>Sleep (the rested buff).</summary>
    Sleep = 5,
}
