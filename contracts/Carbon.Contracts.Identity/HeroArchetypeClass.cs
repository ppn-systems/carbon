// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Contracts.Identity;

/// <summary>
/// Starter combat discipline selected when creating a new hero character.
/// </summary>
public enum HeroArchetypeClass : byte
{
    /// <summary>Frontline melee swordsman starting with <c>IronSword</c> and rugged defense.</summary>
    Vanguard = 0,

    /// <summary>Long-range marksman starting with <c>HuntingBow</c> and leather ranger armor.</summary>
    Ranger = 1,

    /// <summary>Spellcaster mage starting with <c>ArcaneStaff</c>, bonus Mana Tonics, and crystalline focus.</summary>
    Arcanist = 2,
}
