// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Survival.Stamina;

/// <summary>What a primary swing is made with, for its default stamina cost (ADR-033).</summary>
public enum StaminaSwingKind : byte
{
    /// <summary>Empty hand, or an item that is neither a tool nor a weapon (punching, bare-hand gathering).</summary>
    BareHand = 0,

    /// <summary>A melee weapon.</summary>
    Melee = 1,

    /// <summary>A gathering tool: axe, pickaxe.</summary>
    Tool = 2,

    /// <summary>A bow shot (drawing the string).</summary>
    Ranged = 3,

    /// <summary>A staff cast; paid with mana.</summary>
    Spell = 4,
}
