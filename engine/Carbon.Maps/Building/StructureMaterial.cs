// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Maps.Building;

/// <summary>
/// Identifies the raw material composition of a constructed structure, determining durability and resistance.
/// </summary>
public enum StructureMaterial : byte
{
    /// <summary>
    /// Wood timber: inexpensive, combustible, moderate durability (150 HP).
    /// </summary>
    Wood = 0,

    /// <summary>
    /// Cut stone: non-flammable, heavy load-bearing, high durability (400 HP).
    /// </summary>
    Stone = 1,

    /// <summary>
    /// Reinforced metal/steel: industrial, high durability, explosion resistant (550 HP).
    /// </summary>
    Metal = 2
}
