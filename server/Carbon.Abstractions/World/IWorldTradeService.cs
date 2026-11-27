// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Threading.Tasks;

namespace Carbon.Abstractions.World;

/// <summary>
/// Trading with village trader NPCs (ADR-025). Requests are queued and run on the world tick (ADR-017); the outcome
/// completes the returned task. A successful open, buy or sell pushes a fresh trader stock to the player.
/// </summary>
public interface IWorldTradeService
{
    /// <summary>Opens a trader's window for the player on <paramref name="connectionId"/>.</summary>
    ValueTask<WorldMutationOutcome> SubmitTraderOpenAsync(ulong connectionId, ulong traderNetworkId);

    /// <summary>Buys units from a trader stock slot, as priced in <paramref name="epoch"/>.</summary>
    ValueTask<WorldMutationOutcome> SubmitTraderBuyAsync(ulong connectionId, ulong traderNetworkId, byte slotIndex, ushort quantity, long epoch);

    /// <summary>Sells units from a bag slot to a trader.</summary>
    ValueTask<WorldMutationOutcome> SubmitTraderSellAsync(ulong connectionId, ulong traderNetworkId, ushort slotIndex, ushort quantity);
}
