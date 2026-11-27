// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items;
using Carbon.Contracts.Items.C2S;
using Carbon.Handlers.Internal;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;

namespace Carbon.Handlers.Items;

// Trading with village traders (ADR-025). Requests are queued on the world tick (ADR-017); the stock itself is pushed
// with TraderStock when a request succeeds.
public static partial class ItemHandlers
{
    /// <summary>Opcode <c>0x4040</c>. Opens a trader; replies with <c>MutationResult</c> only on failure.</summary>
    [PacketOpcode(ItemOpCode.TraderOpen)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleTraderOpenAsync(IPacketContext<TraderOpenRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Captured before the first await: the pooled context may be recycled while suspended (audit TS-4).
        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        ulong trader = context.Packet.TraderNetworkId;
        WorldMutationOutcome outcome = await s_world.Trade.SubmitTraderOpenAsync(connectionId, trader).ConfigureAwait(false);
        if (outcome.Status != OpStatus.Ok)
        {
            await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.TraderOpen).ConfigureAwait(false);
        }
    }

    /// <summary>Opcode <c>0x4041</c>. Buys from a trader stock slot.</summary>
    [PacketOpcode(ItemOpCode.TraderBuy)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleTraderBuyAsync(IPacketContext<TraderBuyRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        TraderBuyRequest p = context.Packet;
        (ulong trader, byte slot, ushort quantity, long epoch) = (p.TraderNetworkId, p.SlotIndex, p.Quantity, p.Epoch);
        WorldMutationOutcome outcome = await s_world.Trade.SubmitTraderBuyAsync(connectionId, trader, slot, quantity, epoch).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.TraderBuy).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4042</c>. Sells a bag stack to a trader.</summary>
    [PacketOpcode(ItemOpCode.TraderSell)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleTraderSellAsync(IPacketContext<TraderSellRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        TraderSellRequest p = context.Packet;
        (ulong trader, ushort slot, ushort quantity) = (p.TraderNetworkId, p.SlotIndex, p.Quantity);
        WorldMutationOutcome outcome = await s_world.Trade.SubmitTraderSellAsync(connectionId, trader, slot, quantity).ConfigureAwait(false);
        await PacketReplyHelper.SendMutationAsync(sender, outcome.Status, ItemOpCode.TraderSell).ConfigureAwait(false);
    }
}
