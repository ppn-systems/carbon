// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client confirms its sell basket at a nearby trader (ADR-025 D18): parallel lists of bag slot, the item the player saw
/// in it and the units to sell, plus the total gold the window showed. The server recomputes every price and sells all
/// lines or none. Answered with <see cref="S2C.TraderBatchResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TraderSellBatchRequest : PacketBase<TraderSellBatchRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.TraderSellBatch;

    /// <summary>Network id of the trader NPC.</summary>
    [SerializeOrder(0)]
    public ulong TraderNetworkId { get; set; }

    /// <summary>Bag slot per line (bag slots only; equipment is never sold).</summary>
    [SerializeOrder(1)]
    public ushort[] SlotIndices { get; set; } = [];

    /// <summary>Item the player saw in each slot; a different item refuses the batch.</summary>
    [SerializeOrder(2)]
    public ushort[] ItemIds { get; set; } = [];

    /// <summary>Units to sell per line (<c>1..slot quantity</c>).</summary>
    [SerializeOrder(3)]
    public ushort[] Quantities { get; set; } = [];

    /// <summary>Total gold the window showed; a different server total refuses the batch.</summary>
    [SerializeOrder(4)]
    public long ExpectedGold { get; set; }
}
