// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Items.Definitions;

/// <summary>
/// Bitmask capabilities describing what combat and harvesting actions an item can perform effectively.
/// </summary>
[Flags]
public enum ToolCapabilities : byte
{
    /// <summary>No specialized tool/weapon capability.</summary>
    None = 0,

    /// <summary>Designed for felling trees and harvesting flora.</summary>
    ChopFlora = 1 << 0,

    /// <summary>Designed for mining rocks, ores, and crystal nodes.</summary>
    MineMineral = 1 << 1,

    /// <summary>Close-range melee combat blade or cleaver.</summary>
    MeleeWeapon = 1 << 2,

    /// <summary>Ranged bow firing ballistic arrows.</summary>
    RangedBow = 1 << 3,

    /// <summary>Arcane staff / spell focus firing mana projectiles.</summary>
    MagicFocus = 1 << 4,
}
