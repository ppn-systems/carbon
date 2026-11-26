// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Maps.Props;

namespace Carbon.Desktop.Abstractions;

/// <summary>
/// Visual feedback event emitted when a tree, boulder, ore vein, crystal, or shrine prop is struck or destroyed.
/// </summary>
/// <param name="TileX">World tile X coordinate of the prop.</param>
/// <param name="TileY">World tile Y coordinate of the prop.</param>
/// <param name="WorldPosition">World-space base coordinate of the prop.</param>
/// <param name="PropType">Concrete prop type hit.</param>
/// <param name="Category">Prop category used for particle/VFX palette selection.</param>
/// <param name="DamageDealt">Durability damage dealt on this hit.</param>
/// <param name="CurrentDurability">Remaining durability after the hit.</param>
/// <param name="MaxDurability">Maximum durability of the prop.</param>
/// <param name="HarvestTier">Minimum tool tier required to harvest the prop.</param>
/// <param name="IsDestroyed">Whether the hit destroyed the prop.</param>
/// <param name="WasTierBlocked">Whether the equipped tool tier was too low to damage the prop.</param>
public readonly record struct ClientPropImpactEvent(
    int TileX,
    int TileY,
    Vector2 WorldPosition,
    MapPropType PropType,
    PropCategory Category,
    float DamageDealt,
    float CurrentDurability,
    float MaxDurability,
    byte HarvestTier,
    bool IsDestroyed,
    bool WasTierBlocked);
