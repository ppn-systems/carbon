// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Abstractions.Persistence;

/// <summary>
/// Persisted character record storing world transform, vitals, and inventory state.
/// </summary>
/// <param name="Id">Unique character identifier.</param>
/// <param name="AccountId">Owning account identifier.</param>
/// <param name="Name">Character display name.</param>
/// <param name="Level">Character progression level.</param>
/// <param name="AppearancePreset">Avatar visual preset.</param>
/// <param name="PositionX">Last saved world X coordinate.</param>
/// <param name="PositionY">Last saved world Y coordinate.</param>
/// <param name="CurrentHealth">Saved current health.</param>
/// <param name="MaxHealth">Saved maximum health.</param>
/// <param name="SelectedHotbarSlot">Active hotbar slot index.</param>
/// <param name="InventoryBlob">Binary serialized inventory payload.</param>
/// <param name="UpdatedAtUtc">Last persistence timestamp.</param>
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
    DateTimeOffset UpdatedAtUtc);
