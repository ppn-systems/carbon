// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Contracts.Identity;

namespace Carbon.Desktop.Abstractions.Identity;

/// <summary>
/// Character summary entry displayed on the online character selection screen.
/// </summary>
/// <param name="CharacterId">Unique persistent character identifier.</param>
/// <param name="Name">Display character name.</param>
/// <param name="Level">Current character level.</param>
/// <param name="ZoneTier">Danger/progression tier at the saved world position.</param>
/// <param name="PositionX">Saved world X position.</param>
/// <param name="PositionY">Saved world Y position.</param>
/// <param name="AppearancePreset">Saved visual appearance preset index.</param>
/// <param name="Class">Persisted hero archetype class.</param>
/// <param name="Traits">Persisted character progression traits bitmask.</param>
public sealed record CharacterRosterEntry(
    Guid CharacterId,
    string Name,
    ushort Level,
    byte ZoneTier,
    float PositionX,
    float PositionY,
    byte AppearancePreset = 0,
    HeroArchetypeClass Class = HeroArchetypeClass.Vanguard,
    CharacterTraits Traits = CharacterTraits.None);
