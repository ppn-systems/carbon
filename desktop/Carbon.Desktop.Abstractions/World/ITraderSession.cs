// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Trading with village traders (ADR-025), as the client sees it: the stock the server last pushed for the open trader,
/// and the requests the trade window sends. Trades are staged in a <see cref="TradeBasket"/> and only sent as a batch
/// when the player presses Sell or Buy (ADR-025 D18). The server validates every request.
/// </summary>
public interface ITraderSession
{
    /// <summary>Gets the open trader's stock, or <see langword="null"/> when no trader is open.</summary>
    TraderView? ActiveTrader { get; }

    /// <summary>Asks to trade with the trader NPC <paramref name="traderNetworkId"/>; the window opens when its stock arrives.</summary>
    bool TryOpen(ulong traderNetworkId);

    /// <summary>
    /// Sends the sell basket as one all-or-nothing batch for <paramref name="expectedGold"/> (ADR-025 D18); the answer
    /// arrives through <see cref="TryTakeBatchReply"/>.
    /// </summary>
    bool TrySellBatch(ulong traderNetworkId, ReadOnlySpan<TradeBasketLine> lines, long expectedGold);

    /// <summary>Sends the buy cart, priced in <paramref name="epoch"/>, as one all-or-nothing batch for <paramref name="expectedGold"/>.</summary>
    bool TryBuyBatch(ulong traderNetworkId, long epoch, ReadOnlySpan<TradeBasketLine> lines, long expectedGold);

    /// <summary>Takes the oldest server answer to a sent basket or cart (main thread, once a frame).</summary>
    bool TryTakeBatchReply(out TradeBatchReply reply);

    /// <summary>Closes the trader window; a stock pushed later is ignored.</summary>
    void Close();
}
