// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// Server reply to <see cref="C2S.TraderSellBatchRequest"/> and <see cref="C2S.TraderBuyBatchRequest"/> (ADR-025 D18).
/// On success the changed slots, the wallet and a fresh <see cref="TraderStock"/> arrive first; on failure nothing
/// changed, and a <see cref="TradeBatchError.PricesChanged"/> refusal is preceded by a fresh stock to re-confirm against.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TraderBatchResult : PacketBase<TraderBatchResult>, IPacketStaticOpcode
{
    /// <summary>The reply answers a sell basket.</summary>
    public const byte KindSell = 1;

    /// <summary>The reply answers a buy cart.</summary>
    public const byte KindBuy = 2;

    /// <summary>Value of <see cref="Line"/> when no single line is to blame.</summary>
    public const byte NoLine = byte.MaxValue;

    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.TraderBatchResult;

    /// <summary>Network id of the trader NPC.</summary>
    [SerializeOrder(0)]
    public ulong TraderNetworkId { get; set; }

    /// <summary><see cref="KindSell"/> or <see cref="KindBuy"/>.</summary>
    [SerializeOrder(1)]
    public byte Kind { get; set; }

    /// <summary><see cref="OpStatus.Ok"/> when every line was applied, otherwise the queue or world status.</summary>
    [SerializeOrder(2)]
    public OpStatus Status { get; set; }

    /// <summary>Why the batch was refused (<see cref="TradeBatchError.None"/> on success).</summary>
    [SerializeOrder(3)]
    public TradeBatchError Error { get; set; }

    /// <summary>Index of the first line that failed, or <see cref="NoLine"/>.</summary>
    [SerializeOrder(4)]
    public byte Line { get; set; } = NoLine;
}
