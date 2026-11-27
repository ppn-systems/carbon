// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Trading with village traders (ADR-025), as the client sees it: the stock the server last pushed for the open trader,
/// and the requests the trade window sends. The server validates every request; a failure shows a toast.
/// </summary>
public interface ITraderSession
{
    /// <summary>Gets the open trader's stock, or <see langword="null"/> when no trader is open.</summary>
    TraderView? ActiveTrader { get; }

    /// <summary>Asks to trade with the trader NPC <paramref name="traderNetworkId"/>; the window opens when its stock arrives.</summary>
    bool TryOpen(ulong traderNetworkId);

    /// <summary>Buys <paramref name="quantity"/> units from stock slot <paramref name="slotIndex"/> of the open trader.</summary>
    bool TryBuy(byte slotIndex, ushort quantity = 1);

    /// <summary>Sells <paramref name="quantity"/> units from bag slot <paramref name="slotIndex"/> to the open trader.</summary>
    bool TrySell(ushort slotIndex, ushort quantity = 1);

    /// <summary>Closes the trader window; a stock pushed later is ignored.</summary>
    void Close();
}
