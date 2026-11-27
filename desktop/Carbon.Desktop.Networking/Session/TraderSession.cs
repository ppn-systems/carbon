// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Items.C2S;
using Carbon.Contracts.Items.S2C;
using Carbon.Desktop.Abstractions.World;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Codec.DataFrames;
using Nalix.Codec.Pooling;
using Nalix.SDK.Options;
using Nalix.SDK.Transport;
using Nalix.SDK.Transport.Extensions;

namespace Carbon.Desktop.Networking.Session;

/// <summary>
/// Trading with village traders (ADR-025): the server pushes <see cref="TraderStock"/>; the client keeps the last one as
/// <see cref="ActiveTrader"/> while the window is open and sends the committed basket or cart as one batch (ADR-025 D18),
/// queuing the answer for the window and showing a toast when one is refused.
/// </summary>
/// <param name="tcp">Gets the TCP session while the world session is ready, else <see langword="null"/>.</param>
/// <param name="notify">Queues a HUD toast (thread-safe).</param>
public sealed class TraderSession(Func<TcpSession?> tcp, Action<string> notify) : ITraderSession
{
    private readonly Func<TcpSession?> _tcp = tcp ?? throw new ArgumentNullException(nameof(tcp));
    private readonly Action<string> _notify = notify ?? throw new ArgumentNullException(nameof(notify));
    private readonly ConcurrentQueue<TradeBatchReply> _replies = new();
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
    public bool TrySellBatch(ulong traderNetworkId, ReadOnlySpan<TradeBasketLine> lines, long expectedGold)
    {
        if (!this.CanCommit(traderNetworkId, lines) || this.Connected() is not TcpSession session)
        {
            return false;
        }

        ushort[] slots = new ushort[lines.Length];
        ushort[] items = new ushort[lines.Length];
        ushort[] quantities = new ushort[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            slots[i] = (ushort)lines[i].Slot;
            items[i] = (ushort)lines[i].ItemId;
            quantities[i] = (ushort)Math.Min(ushort.MaxValue, lines[i].Quantity);
        }

        _ = this.CommitAsync<TraderSellBatchRequest>(session, traderNetworkId, TraderBatchResult.KindSell, p =>
        {
            p.TraderNetworkId = traderNetworkId;
            p.SlotIndices = slots;
            p.ItemIds = items;
            p.Quantities = quantities;
            p.ExpectedGold = expectedGold;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TryBuyBatch(ulong traderNetworkId, long epoch, ReadOnlySpan<TradeBasketLine> lines, long expectedGold)
    {
        if (!this.CanCommit(traderNetworkId, lines) || this.Connected() is not TcpSession session)
        {
            return false;
        }

        byte[] slots = new byte[lines.Length];
        ushort[] items = new ushort[lines.Length];
        ushort[] quantities = new ushort[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            slots[i] = (byte)lines[i].Slot;
            items[i] = (ushort)lines[i].ItemId;
            quantities[i] = (ushort)Math.Min(ushort.MaxValue, lines[i].Quantity);
        }

        _ = this.CommitAsync<TraderBuyBatchRequest>(session, traderNetworkId, TraderBatchResult.KindBuy, p =>
        {
            p.TraderNetworkId = traderNetworkId;
            p.Epoch = epoch;
            p.StockSlots = slots;
            p.ItemIds = items;
            p.Quantities = quantities;
            p.ExpectedGold = expectedGold;
        });
        return true;
    }

    /// <inheritdoc/>
    public bool TryTakeBatchReply(out TradeBatchReply reply) => _replies.TryDequeue(out reply);

    /// <inheritdoc/>
    public void Close()
    {
        Volatile.Write(ref _requestedTrader, 0UL);
        Volatile.Write(ref _activeTrader, null);
    }

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

    private bool CanCommit(ulong traderNetworkId, ReadOnlySpan<TradeBasketLine> lines) =>
        traderNetworkId != 0UL && !lines.IsEmpty && lines.Length <= TradeBasket.MaxSellLines && this.ActiveTrader?.TraderNetworkId == traderNetworkId;

    // Sends a basket or cart and queues the server's answer for the window; a lost request answers as a plain failure,
    // so the window never waits forever.
    private async Task CommitAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TPacket>(TcpSession session, ulong traderNetworkId, byte kind, Action<TPacket> populate)
        where TPacket : PacketBase<TPacket>, IPacketStaticOpcode, new()
    {
        TradeBatchReply answer;
        try
        {
            RequestOptions options = RequestOptions.Default.WithTimeout(4000).WithEncrypt(session.State.EncryptionEnabled);
            using PacketScope<TPacket> lease = PacketFactory<TPacket>.Acquire();
            populate(lease.Value);
            using TraderBatchResult reply = await session.RequestAsync<TraderBatchResult>(lease.Value, options, predicate: null, CancellationToken.None).ConfigureAwait(false);
            answer = new TradeBatchReply(reply.TraderNetworkId, reply.Kind, reply.Status, reply.Error, reply.Line);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            answer = new TradeBatchReply(traderNetworkId, kind, OpStatus.Invalid, TradeBatchError.None, TraderBatchResult.NoLine);
        }

        _replies.Enqueue(answer);
        if (!answer.IsOk)
        {
            _notify(TradeBasket.DescribeFailure(answer));
        }
    }
}
