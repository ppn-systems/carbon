// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Contracts.Common;

namespace Carbon.Abstractions.World;

/// <summary>
/// Role-segregated real-time player movement, combat/skill input, and ground loot pickup command sink (ISP).
/// </summary>
public interface IWorldEntityCommandSink
{
    /// <summary>Enqueues a client movement input command for the next fixed simulation tick.</summary>
    void EnqueueMoveInput(ulong connectionId, uint clientTick, Vector2 direction, bool sprint);

    /// <summary>Enqueues a client combat swing, skill activation, or consumable action onto the next fixed tick.</summary>
    OpStatus EnqueueAttackInput(ulong connectionId, ulong targetNetworkId, Vector2 aimWorldPosition, byte abilitySlot);

    /// <summary>Enqueues an explicit pickup attempt for a nearby ground loot entity.</summary>
    OpStatus EnqueuePickupLoot(ulong connectionId, ulong lootNetworkId);

    /// <summary>Allocates unspent attribute points or skill mastery points for <paramref name="connectionId"/>.</summary>
    OpStatus TryAllocateCharacterPoint(
        ulong connectionId,
        Contracts.Entities.PointAllocationKind kind,
        ushort targetId,
        byte amount = 1);

    /// <summary>
    /// Starts resting (sleeping in a bed within reach, otherwise sitting) or stands the player up (ADR-017).
    /// </summary>
    /// <param name="connectionId">Requesting connection.</param>
    /// <param name="rest"><see langword="true"/> to rest, <see langword="false"/> to stand up.</param>
    /// <returns><see cref="OpStatus.Ok"/>, <see cref="OpStatus.Forbidden"/> when not in the world or dead.</returns>
    OpStatus TrySetResting(ulong connectionId, bool rest);
}
