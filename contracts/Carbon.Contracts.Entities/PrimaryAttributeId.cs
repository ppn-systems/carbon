// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Entities;

/// <summary>
/// Open attribute identifiers allocatable via <see cref="C2S.EntityAllocatePoint"/> and persisted in progression blobs.
/// Per-point effects come from the server's <c>progression.json</c> (ADR-021); ids are persisted, so never renumber.
/// </summary>
public enum PrimaryAttributeId : ushort
{
    /// <summary>Vitality: max health, defense and health regeneration.</summary>
    Vitality = 0,

    /// <summary>Wisdom: max mana, spell power, magic resist and mana regeneration.</summary>
    Wisdom = 1,

    /// <summary>Power: physical attack.</summary>
    Power = 2,

    /// <summary>Agility: attack speed, movement speed, evasion and stamina.</summary>
    Agility = 3,

    /// <summary>Dexterity: critical chance, critical damage and a little attack.</summary>
    Dexterity = 4,
}
