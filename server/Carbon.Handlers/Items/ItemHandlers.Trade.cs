// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Threading.Tasks;
using Carbon.Abstractions.World;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Handlers.Internal;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Security;
using Nalix.Codec.Pooling;

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

    /// <summary>Opcode <c>0x4052</c>. Sells a whole basket, all or nothing; replies with <see cref="TraderBatchResult"/> (ADR-025 D18).</summary>
    [PacketOpcode(ItemOpCode.TraderSellBatch)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleTraderSellBatchAsync(IPacketContext<TraderSellBatchRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Copied before the first await: the pooled packet and its lists may be recycled while suspended.
        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        TraderSellBatchRequest p = context.Packet;
        ulong trader = p.TraderNetworkId;
        WorldMutationOutcome outcome = await s_world.Trade.SubmitTraderSellBatchAsync(
            connectionId,
            trader,
            [.. p.SlotIndices],
            [.. p.ItemIds],
            [.. p.Quantities],
            p.ExpectedGold).ConfigureAwait(false);
        await ReplyBatchAsync(sender, trader, TraderBatchResult.KindSell, outcome).ConfigureAwait(false);
    }

    /// <summary>Opcode <c>0x4053</c>. Buys a whole cart, all or nothing; replies with <see cref="TraderBatchResult"/> (ADR-025 D18).</summary>
    [PacketOpcode(ItemOpCode.TraderBuyBatch)]
    [PacketPermission(PermissionLevel.USER)]
    public static async ValueTask HandleTraderBuyBatchAsync(IPacketContext<TraderBuyBatchRequest> context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IPacketSender sender = context.Sender;
        ulong connectionId = s_transports.ResolveLogicalConnectionId(context.Connection);
        TraderBuyBatchRequest p = context.Packet;
        ulong trader = p.TraderNetworkId;
        WorldMutationOutcome outcome = await s_world.Trade.SubmitTraderBuyBatchAsync(
            connectionId,
            trader,
            p.Epoch,
            [.. p.StockSlots],
            [.. p.ItemIds],
            [.. p.Quantities],
            p.ExpectedGold).ConfigureAwait(false);
        await ReplyBatchAsync(sender, trader, TraderBatchResult.KindBuy, outcome).ConfigureAwait(false);
    }

    private static async Task ReplyBatchAsync(IPacketSender sender, ulong trader, byte kind, WorldMutationOutcome outcome)
    {
        TradeBatchReceipt receipt = TradeBatchReceipt.Unpack(outcome.Revision);
        using PacketScope<TraderBatchResult> lease = PacketFactory<TraderBatchResult>.Acquire();
        TraderBatchResult reply = lease.Value;
        reply.TraderNetworkId = trader;
        reply.Kind = kind;
        reply.Status = outcome.Status;
        reply.Error = outcome.Status == OpStatus.Ok ? TradeBatchError.None : receipt.Error;
        reply.Line = outcome.Status == OpStatus.Ok ? TraderBatchResult.NoLine : receipt.Line;
        await sender.ReplyAsync(reply).ConfigureAwait(false);
    }
}
