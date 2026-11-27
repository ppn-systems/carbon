// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client sells <see cref="Quantity"/> units from bag slot <see cref="SlotIndex"/> to a nearby trader at the economy's
/// vendor sell price. Answered with <see cref="Common.MutationResult"/> (ADR-025).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TraderSellRequest : PacketBase<TraderSellRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.TraderSell;

    /// <summary>Network id of the trader NPC.</summary>
    [SerializeOrder(0)]
    public ulong TraderNetworkId { get; set; }

    /// <summary>Bag slot to sell from.</summary>
    [SerializeOrder(1)]
    public ushort SlotIndex { get; set; }

    /// <summary>Units to sell (<c>1..slot quantity</c>).</summary>
    [SerializeOrder(2)]
    public ushort Quantity { get; set; } = 1;
}
