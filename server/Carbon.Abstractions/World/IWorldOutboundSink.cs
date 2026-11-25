// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Contracts.Entities;

namespace Carbon.Abstractions.World;

/// <summary>
/// Snapshot sent by the authoritative simulation when an entity enters a player's Area of Interest.
/// </summary>
/// <param name="NetworkId">Authoritative entity network ID.</param>
/// <param name="Kind">Entity kind.</param>
/// <param name="ArchetypeId">Visual/archetype identifier.</param>
/// <param name="Name">Entity display name.</param>
/// <param name="PositionX">World X position.</param>
/// <param name="PositionY">World Y position.</param>
/// <param name="VelocityX">Linear X velocity.</param>
/// <param name="VelocityY">Linear Y velocity.</param>
/// <param name="Radius">Collider radius.</param>
/// <param name="CurrentHealth">Current health points.</param>
/// <param name="MaxHealth">Maximum health points.</param>
public readonly record struct EntitySpawnSnapshot(
    ulong NetworkId,
    EntityKind Kind,
    ushort ArchetypeId,
    string Name,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    float Radius,
    float CurrentHealth,
    float MaxHealth);

/// <summary>
/// Per-entity delta slice collected inside a tick's AoI snapshot batch.
/// </summary>
/// <param name="NetworkId">Entity network ID.</param>
/// <param name="DirtyMask">Dirty bitmask flags.</param>
/// <param name="PositionX">World X coordinate.</param>
/// <param name="PositionY">World Y coordinate.</param>
/// <param name="VelocityX">Linear X velocity.</param>
/// <param name="VelocityY">Linear Y velocity.</param>
/// <param name="CurrentHealth">Current health points.</param>
public readonly record struct EntityDeltaEntry(
    ulong NetworkId,
    byte DirtyMask,
    float PositionX,
    float PositionY,
    float VelocityX,
    float VelocityY,
    float CurrentHealth);

/// <summary>
/// Decoupled transport sink used by <c>Carbon.Application</c> to dispatch AoI entity updates and map edits
/// to active connections without referencing Nalix sockets directly.
/// </summary>
public interface IWorldOutboundSink
{
    /// <summary>Sends an entity spawn message to the target connection.</summary>
    void SendEntitySpawn(ulong connectionId, in EntitySpawnSnapshot spawn);

    /// <summary>Sends an entity despawn notification to the target connection.</summary>
    void SendEntityDespawn(ulong connectionId, ulong networkId, ulong serverTick);

    /// <summary>Sends a batched AoI entity delta update to the target connection.</summary>
    void SendEntityDeltaBatch(
        ulong connectionId,
        ulong serverTick,
        uint lastProcessedClientTick,
        float selfX,
        float selfY,
        ReadOnlySpan<EntityDeltaEntry> entries);

    /// <summary>Broadcasts a natural prop state update to connections in range.</summary>
    void SendPropChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, uint revision, float durability, bool isDestroyed);

    /// <summary>Broadcasts a constructed structure update to connections in range.</summary>
    void SendStructureChanged(ReadOnlySpan<ulong> recipientConnectionIds, int tileX, int tileY, byte structureType, float durability, byte doorState, byte connectivity);

    /// <summary>Broadcasts a combat damage event to connections observing the combatants.</summary>
    void SendCombatHit(ReadOnlySpan<ulong> recipientConnectionIds, ulong attackerNetworkId, ulong defenderNetworkId, float damage, float remainingHealth, bool isCritical, bool isKilled);

    /// <summary>Sends a single updated inventory slot to the owning player connection.</summary>
    void SendInventorySlotChanged(ulong connectionId, ushort slotIndex, uint itemId, int quantity, float durability);
}
