// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Identity;

namespace Carbon.Abstractions.Persistence;

/// <summary>
/// Persisted character record storing world transform, vitals, archetype class, bitmask flags,
/// inventory blob, and extensible binary progression payload (<c>attributes + N skills</c>).
/// </summary>
public sealed record CharacterRecord(
    Guid Id,
    Guid AccountId,
    string Name,
    ushort Level,
    byte AppearancePreset,
    float PositionX,
    float PositionY,
    float CurrentHealth,
    float MaxHealth,
    byte SelectedHotbarSlot,
    byte[] InventoryBlob,
    DateTimeOffset UpdatedAtUtc,
    uint Experience = 0U,
    float CurrentMana = 80f,
    float MaxMana = 80f,
    float CurrentStamina = 100f,
    float MaxStamina = 100f,
    HeroArchetypeClass Class = HeroArchetypeClass.Vanguard,
    CharacterTraits Flags = CharacterTraits.StarterLoadoutGranted,
    CharacterSkillMask UnlockedSkills = CharacterSkillMask.None,
    byte[]? ProgressionBlob = null);
