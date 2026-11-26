// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using System.Threading.Tasks;
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

    /// <summary>
    /// Queues allocating unspent attribute points or skill mastery points for <paramref name="connectionId"/>; completes
    /// after the next tick (ADR-017).
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitAllocateCharacterPointAsync(
        ulong connectionId,
        Contracts.Entities.PointAllocationKind kind,
        ushort targetId,
        byte amount = 1);
}
