// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client confirms its buy cart at a nearby trader (ADR-025 D18): parallel lists of stock slot, the item the player saw
/// in it and the units to buy, priced in restock epoch <see cref="Epoch"/> for the total <see cref="ExpectedGold"/>. The
/// server recomputes every price and buys all lines or none. Answered with <see cref="S2C.TraderBatchResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TraderBuyBatchRequest : PacketBase<TraderBuyBatchRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.TraderBuyBatch;

    /// <summary>Network id of the trader NPC.</summary>
    [SerializeOrder(0)]
    public ulong TraderNetworkId { get; set; }

    /// <summary>Restock epoch of the stock the player is looking at.</summary>
    [SerializeOrder(1)]
    public long Epoch { get; set; }

    /// <summary>Stock slot per line, from <see cref="S2C.TraderStock"/>.</summary>
    [SerializeOrder(2)]
    public byte[] StockSlots { get; set; } = [];

    /// <summary>Item the player saw in each stock slot.</summary>
    [SerializeOrder(3)]
    public ushort[] ItemIds { get; set; } = [];

    /// <summary>Units to buy per line.</summary>
    [SerializeOrder(4)]
    public ushort[] Quantities { get; set; } = [];

    /// <summary>Total gold the window showed; a different server total refuses the batch.</summary>
    [SerializeOrder(5)]
    public long ExpectedGold { get; set; }
}
