// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Definitions;

/// <summary>
/// Equipment slot position on an actor's paper-doll loadout.
/// </summary>
public enum EquipmentSlotKind : byte
{
    /// <summary>Not equippable.</summary>
    None = 0,

    /// <summary>Primary hand weapon slot.</summary>
    MainHand = 1,

    /// <summary>Off-hand shield or focus slot.</summary>
    OffHand = 2,

    /// <summary>Dedicated harvesting tool slot.</summary>
    Tool = 3,

    /// <summary>Head armor slot.</summary>
    Head = 4,

    /// <summary>Chest armor slot.</summary>
    Chest = 5,

    /// <summary>Leg armor slot.</summary>
    Legs = 6,

    /// <summary>Footwear slot.</summary>
    Feet = 7,

    /// <summary>First accessory slot.</summary>
    Accessory1 = 8,

    /// <summary>Second accessory slot.</summary>
    Accessory2 = 9
}
