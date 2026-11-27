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
/// <param name="tcp">Gets the TCP session while the world session is ready, else <see langword="null"/>.</param>
/// <param name="notify">Queues a HUD toast (thread-safe).</param>
public sealed class TraderSession(Func<TcpSession?> tcp, Action<string> notify) : ITraderSession
{
    private readonly Func<TcpSession?> _tcp = tcp ?? throw new ArgumentNullException(nameof(tcp));
    private readonly Action<string> _notify = notify ?? throw new ArgumentNullException(nameof(notify));
    private TraderView? _activeTrader;
    private ulong _requestedTrader;

    /// <inheritdoc/>
    public TraderView? ActiveTrader => Volatile.Read(ref _activeTrader);

    /// <inheritdoc/>
    public bool TryOpen(ulong traderNetworkId)
    {
        if (traderNetworkId == 0UL || this.Connected() is not TcpSession session)
        {
            return false;
        }

        Volatile.Write(ref _requestedTrader, traderNetworkId);
        _ = SendAsync<TraderOpenRequest>(session, p => p.TraderNetworkId = traderNetworkId);
        return true;
    }

    /// <inheritdoc/>
    public bool TryBuy(byte slotIndex, ushort quantity = 1)
    {
        TraderView? trader = this.ActiveTrader;
        if (trader is null || quantity == 0 || slotIndex >= trader.ItemIds.Length || this.Connected() is not TcpSession session)
        {
            return false;
        }

        _ = this.RequestAsync<TraderBuyRequest>(session, p =>
        {
            p.TraderNetworkId = trader.TraderNetworkId;
            p.SlotIndex = slotIndex;
            p.Quantity = quantity;
            p.Epoch = trader.Epoch;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TrySell(ushort slotIndex, ushort quantity = 1)
    {
        TraderView? trader = this.ActiveTrader;
        if (trader is null || quantity == 0 || slotIndex >= PlayerInventoryLayout.BagLimit || this.Connected() is not TcpSession session)
        {
            return false;
        }

        _ = this.RequestAsync<TraderSellRequest>(session, p =>
        {
            p.TraderNetworkId = trader.TraderNetworkId;
            p.SlotIndex = slotIndex;
            p.Quantity = quantity;
        });
        return true;
    }

    /// <inheritdoc/>
    public void Close()
    {
        Volatile.Write(ref _requestedTrader, 0UL);
        Volatile.Write(ref _activeTrader, null);
    }

    /// <summary>Gets the localized toast for a failed trade.</summary>
    public static string DescribeFailure(OpStatus status) => status switch
    {
        OpStatus.Conflict => GameText.Get("trade.failed_conflict"),
        OpStatus.InventoryFull => GameText.Get("trade.failed_bag_full"),
        OpStatus.OutOfRange => GameText.Get("trade.failed_too_far"),
        OpStatus.Ok or OpStatus.Forbidden or OpStatus.NotFound or OpStatus.Invalid or OpStatus.RateLimited or
        OpStatus.EnhancementFailed => GameText.Get("trade.failed"),
        _ => GameText.Get("trade.failed"),
    };

    /// <summary>Handles a pushed <see cref="TraderStock"/> (network thread).</summary>
    public void OnStock(TraderStock pkt)
    {
        ArgumentNullException.ThrowIfNull(pkt);

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
            pkt.CanRepair);
        Volatile.Write(ref _activeTrader, view);
    }

    private TcpSession? Connected() => _tcp() is { IsConnected: true } session ? session : null;

    private static async Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TPacket>(TcpSession session, Action<TPacket> populate)
        where TPacket : PacketBase<TPacket>, IPacketStaticOpcode, new()
    {
        try
        {
            using PacketScope<TPacket> lease = PacketFactory<TPacket>.Acquire();
            populate(lease.Value);
            await session.SendAsync(lease.Value, session.State.EncryptionEnabled, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A dropped connection is handled by the session's own fault path; the window just stays as it was.
        }
    }

    private async Task RequestAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TPacket>(TcpSession session, Action<TPacket> populate)
        where TPacket : PacketBase<TPacket>, IPacketStaticOpcode, new()
    {
        OpStatus status = await RequestStatusAsync(session, populate).ConfigureAwait(false);
        if (status != OpStatus.Ok)
        {
            _notify(DescribeFailure(status));
        }
    }

    private static async Task<OpStatus> RequestStatusAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TPacket>(TcpSession session, Action<TPacket> populate)
        where TPacket : PacketBase<TPacket>, IPacketStaticOpcode, new()
    {
        OpStatus status;
        try
        {
            RequestOptions options = RequestOptions.Default.WithTimeout(4000).WithEncrypt(session.State.EncryptionEnabled);
            using PacketScope<TPacket> lease = PacketFactory<TPacket>.Acquire();
            populate(lease.Value);
            using MutationResult reply = await session.RequestAsync<MutationResult>(lease.Value, options, predicate: null, CancellationToken.None).ConfigureAwait(false);
            status = reply.Status;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            status = OpStatus.Invalid;
        }

        return status;
    }
}
