// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>The server's answer to a committed basket or cart (<c>TraderBatchResult</c>, ADR-025 D19).</summary>
/// <param name="TraderNetworkId">Trader NPC network id.</param>
/// <param name="Kind"><c>TraderBatchResult.KindSell</c> or <c>KindBuy</c>.</param>
/// <param name="Status">Wire status; <see cref="OpStatus.Ok"/> when every line was applied.</param>
/// <param name="Error">Why it was refused.</param>
/// <param name="Line">Index of the failing line, or 255.</param>
public readonly record struct TradeBatchReply(ulong TraderNetworkId, byte Kind, OpStatus Status, TradeBatchError Error, byte Line)
{
    /// <summary>Gets whether every line was applied.</summary>
    public bool IsOk => this.Status == OpStatus.Ok;
}
