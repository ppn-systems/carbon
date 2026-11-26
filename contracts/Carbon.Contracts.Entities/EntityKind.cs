// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Entities;

/// <summary>
/// Categorizes networked world entities replicated to clients.
/// Append-only values for wire compatibility.
/// </summary>
public enum EntityKind : byte
{
    /// <summary>Human player avatar.</summary>
    Player = 0,

    /// <summary>Friendly non-player character.</summary>
    Npc = 1,

    /// <summary>Hostile monster or boss.</summary>
    Monster = 2,

    /// <summary>Active projectile in flight.</summary>
    Projectile = 3,

    /// <summary>Dropped item pickup on the ground.</summary>
    LootDrop = 4,
}
