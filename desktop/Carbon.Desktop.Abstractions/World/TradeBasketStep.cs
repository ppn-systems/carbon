// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.Desktop.Abstractions.World;

/// <summary>What a press of Sell or Buy did (ADR-025 D18, D21).</summary>
public enum TradeBasketStep : byte
{
    /// <summary>Nothing to commit, or the request could not be sent.</summary>
    Nothing = 0,

    /// <summary>The batch was sent; the window waits for the server's answer.</summary>
    Sent = 1,

    /// <summary>Prices changed since the player looked: this press accepted the new total, the next one sends.</summary>
    ConfirmPrices = 2,

    /// <summary>The basket holds an enhanced item: this press acknowledged the warning, the next one sends.</summary>
    ConfirmEnhanced = 3,

    /// <summary>The cart costs more than the wallet holds.</summary>
    NotEnoughGold = 4,

    /// <summary>A batch of this kind is still waiting for its answer.</summary>
    Busy = 5,
}
