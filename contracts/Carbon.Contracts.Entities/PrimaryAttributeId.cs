// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Entities;

/// <summary>
/// Open attribute identifiers allocatable via <see cref="C2S.EntityAllocatePoint"/> and persisted in progression blobs.
/// </summary>
public enum PrimaryAttributeId : ushort
{
    /// <summary>Vitality (+12 Max Health, +0.5 Defense per point).</summary>
    Vitality = 0,

    /// <summary>Wisdom (+10 Max Mana, +0.35 MP/s regeneration per point).</summary>
    Wisdom = 1,

    /// <summary>Power (+3.5% Attack &amp; Skill Power multiplier per point).</summary>
    Power = 2,

    /// <summary>Agility (+8 Max Stamina, +0.6% Movement Speed per point).</summary>
    Agility = 3,
}
