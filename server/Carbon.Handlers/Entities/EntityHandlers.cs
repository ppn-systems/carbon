// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Contracts.Entities.C2S;
using Carbon.Handlers.Internal;
using Nalix.Abstractions.Injection;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;

namespace Carbon.Handlers.Entities;

/// <summary>
/// Hot-path stateless packet handlers enqueuing player movement and combat inputs onto the authoritative tick queue.
/// </summary>
[PacketHandler("Carbon.Entities")]
public static partial class EntityHandlers
{
    [Inject] private static IAuthoritativeWorld s_world = null!;

    /// <summary>Opcode <c>0x1201</c>. Enqueues client directional movement onto the next fixed world tick.</summary>
    [PacketOpcode(EntityOpCode.MoveInput)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleMoveInputAsync(IPacketContext<EntityMoveInput> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        EntityMoveInput req = context.Packet;
        s_world.EnqueueMoveInput(
            context.Connection.ConnectionId,
            req.ClientTick,
            new Vector2(req.DirectionX, req.DirectionY),
            req.Sprint);

        return ValueTask.CompletedTask;
    }

    /// <summary>Opcode <c>0x1202</c>. Validates and queues a combat attack input.</summary>
    [PacketOpcode(EntityOpCode.AttackInput)]
    [PacketPermission(PermissionLevel.USER)]
    public static ValueTask HandleAttackInputAsync(IPacketContext<EntityAttackInput> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        EntityAttackInput req = context.Packet;
        OpStatus status = s_world.EnqueueAttackInput(
            context.Connection.ConnectionId,
            req.TargetNetworkId,
            new Vector2(req.AimX, req.AimY),
            req.AbilitySlot);

        return PacketReplyHelper.SendMutationAsync(context, status, EntityOpCode.AttackInput);
    }
}
