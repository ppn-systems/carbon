// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Items.Progression;

/// <summary>Which rating mitigates a hit: <see cref="StatBlock.Defense"/> or <see cref="StatBlock.MagicResist"/>.</summary>
public enum DamageKind : byte
{
    /// <summary>Weapons, claws, arrows: mitigated by defense.</summary>
    Physical = 0,

    /// <summary>Spells and magic bolts: mitigated by magic resist.</summary>
    Magical = 1,
}
