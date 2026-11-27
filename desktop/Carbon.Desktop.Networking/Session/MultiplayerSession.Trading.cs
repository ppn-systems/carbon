// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Items.Catalog;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.DataFrames;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Trading with village traders (ADR-025): the server pushes <see cref="TraderStock"/>; the client keeps the last one as
/// <see cref="ActiveTrader"/> while the window is open and sends buy / sell requests, showing a toast when one fails.
/// </summary>
public sealed partial class MultiplayerSession
{
    private TraderView? _activeTrader;
    private ulong _requestedTrader;

    /// <inheritdoc/>
    public TraderView? ActiveTrader => Volatile.Read(ref _activeTrader);

    /// <inheritdoc/>
    public bool TryOpenTrader(ulong traderNetworkId)
    {
        if (!this.IsReady || _tcpSession is not { IsConnected: true } || traderNetworkId == 0UL)
        {
            return false;
        }

        Volatile.Write(ref _requestedTrader, traderNetworkId);
        _ = this.SendTcpPooledAsync<TraderOpenRequest>(p => p.TraderNetworkId = traderNetworkId);
        return true;
    }

    /// <inheritdoc/>
    public bool TryTraderBuy(byte slotIndex, ushort quantity = 1)
    {
        TraderView? trader = this.ActiveTrader;
        if (trader is null || quantity == 0 || slotIndex >= trader.ItemIds.Length || _tcpSession is not { IsConnected: true } tcp)
        {
            return false;
        }

        _ = this.RequestTradeAsync<TraderBuyRequest>(tcp, p =>
        {
            p.TraderNetworkId = trader.TraderNetworkId;
            p.SlotIndex = slotIndex;
            p.Quantity = quantity;
            p.Epoch = trader.Epoch;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TryTraderSell(ushort slotIndex, ushort quantity = 1)
    {
        TraderView? trader = this.ActiveTrader;
        if (trader is null || quantity == 0 || slotIndex >= PlayerInventoryLayout.BagLimit || _tcpSession is not { IsConnected: true } tcp)
        {
            return false;
        }

        _ = this.RequestTradeAsync<TraderSellRequest>(tcp, p =>
        {
            p.TraderNetworkId = trader.TraderNetworkId;
            p.SlotIndex = slotIndex;
            p.Quantity = quantity;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TryTraderRepair(ushort slotIndex)
    {
        TraderView? trader = this.ActiveTrader;
        if (trader is not { CanRepair: true } || trader.RepairCostOf(slotIndex) <= 0 || _tcpSession is not { IsConnected: true } tcp)
        {
            return false;
        }

        _ = this.RepairAtTraderAsync(tcp, trader.TraderNetworkId, slotIndex);
        return true;
    }

    /// <inheritdoc/>
    public void CloseTrader()
    {
        Volatile.Write(ref _requestedTrader, 0UL);
        Volatile.Write(ref _activeTrader, null);
    }

    /// <summary>Gets the localized toast for a failed trade (shared by the trade window).</summary>
    public static string DescribeTradeFailure(OpStatus status) => status switch
    {
        OpStatus.Conflict => GameText.Get("trade.failed_conflict"),
        OpStatus.InventoryFull => GameText.Get("trade.failed_bag_full"),
        OpStatus.OutOfRange => GameText.Get("trade.failed_too_far"),
        OpStatus.Ok or OpStatus.Forbidden or OpStatus.NotFound or OpStatus.Invalid or OpStatus.RateLimited or
        OpStatus.EnhancementFailed => GameText.Get("trade.failed"),
        _ => GameText.Get("trade.failed"),
    };

    private void OnTraderStock(TraderStock pkt)
    {
        // Only the trader the player asked for opens (a late push after closing the window is ignored).
        if (pkt.TraderNetworkId != Volatile.Read(ref _requestedTrader))
        {
            return;
        }

        TraderView view = new(
            pkt.TraderNetworkId,
            pkt.Kind,
            pkt.Epoch,
            DateTimeOffset.UtcNow.AddSeconds(pkt.SecondsUntilRestock),
            [.. pkt.ItemIds],
            [.. pkt.UnitPrices],
            [.. pkt.Stocked],
            [.. pkt.Remaining],
            [.. pkt.SellItemIds],
            [.. pkt.SellPrices],
            pkt.CanRepair,
            [.. pkt.RepairSlots],
            [.. pkt.RepairCosts]);
        Volatile.Write(ref _activeTrader, view);
    }

    // Forge repair (RepairInventoryItem) through the blacksmith's window; on success the window asks for fresh quotes.
    private async Task RepairAtTraderAsync(TcpSession tcp, ulong traderNetworkId, ushort slotIndex)
    {
        OpStatus status;
        try
        {
            RequestOptions options = RequestOptions.Default.WithTimeout(4000).WithEncrypt(tcp.State.EncryptionEnabled);
            using PacketScope<RepairInventoryItem> lease = PacketFactory<RepairInventoryItem>.Acquire();
            lease.Value.SlotIndex = slotIndex;
            using MutationResult reply = await tcp.RequestAsync<MutationResult>(lease.Value, options, predicate: null, CancellationToken.None).ConfigureAwait(false);
            status = reply.Status;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            status = OpStatus.Invalid;
        }

        if (status == OpStatus.Ok)
        {
            _ = this.TryOpenTrader(traderNetworkId);
            return;
        }

        lock (_worldGate)
        {
            _hudNotifications.Add(status == OpStatus.Conflict ? GameText.Get("trade.failed_repair") : DescribeTradeFailure(status));
        }
    }

    private async Task RequestTradeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TPacket>(TcpSession tcp, Action<TPacket> populate)
        where TPacket : PacketBase<TPacket>, IPacketStaticOpcode, new()
    {
        try
        {
            RequestOptions options = RequestOptions.Default.WithTimeout(4000).WithEncrypt(tcp.State.EncryptionEnabled);
            using PacketScope<TPacket> lease = PacketFactory<TPacket>.Acquire();
            populate(lease.Value);
            using MutationResult reply = await tcp.RequestAsync<MutationResult>(lease.Value, options, predicate: null, CancellationToken.None).ConfigureAwait(false);
            if (reply.Status != OpStatus.Ok)
            {
                lock (_worldGate)
                {
                    _hudNotifications.Add(DescribeTradeFailure(reply.Status));
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            lock (_worldGate)
            {
                _hudNotifications.Add(GameText.Get("trade.failed"));
            }
        }
    }
}
