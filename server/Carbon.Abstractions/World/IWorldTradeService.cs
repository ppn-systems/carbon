// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading.Tasks;

namespace Carbon.Abstractions.World;

/// <summary>
/// Trading with village trader NPCs (ADR-025). Requests are queued and run on the world tick (ADR-017); the outcome
/// completes the returned task. A successful open, buy or sell batch pushes a fresh trader stock to the player.
/// </summary>
public interface IWorldTradeService
{
    /// <summary>Opens a trader's window for the player on <paramref name="connectionId"/>.</summary>
    ValueTask<WorldMutationOutcome> SubmitTraderOpenAsync(ulong connectionId, ulong traderNetworkId);

    /// <summary>
    /// Sells a whole basket (parallel lists of bag slot, expected item and units) for <paramref name="expectedGold"/>, all
    /// or nothing (ADR-025 D18). The outcome's <see cref="WorldMutationOutcome.Revision"/> is a packed
    /// <see cref="TradeBatchReceipt"/>. The lists are owned by the queue once submitted.
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitTraderSellBatchAsync(ulong connectionId, ulong traderNetworkId, ushort[] slotIndices, ushort[] itemIds, ushort[] quantities, long expectedGold);

    /// <summary>
    /// Buys a whole cart (parallel lists of stock slot, expected item and units) priced in <paramref name="epoch"/> for
    /// <paramref name="expectedGold"/>, all or nothing (ADR-025 D18). The outcome's
    /// <see cref="WorldMutationOutcome.Revision"/> is a packed <see cref="TradeBatchReceipt"/>.
    /// </summary>
    ValueTask<WorldMutationOutcome> SubmitTraderBuyBatchAsync(ulong connectionId, ulong traderNetworkId, long epoch, byte[] stockSlots, ushort[] itemIds, ushort[] quantities, long expectedGold);
}
