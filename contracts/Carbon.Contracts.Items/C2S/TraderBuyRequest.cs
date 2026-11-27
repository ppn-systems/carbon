// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client buys <see cref="Quantity"/> units from stock slot <see cref="SlotIndex"/> of a trader, as priced in restock
/// epoch <see cref="Epoch"/>. A purchase against an older epoch is refused (<c>Conflict</c>). Answered with
/// <see cref="Common.MutationResult"/>; on success the server pushes a fresh <see cref="S2C.TraderStock"/> (ADR-025).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class TraderBuyRequest : PacketBase<TraderBuyRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.TraderBuy;

    /// <summary>Network id of the trader NPC.</summary>
    [SerializeOrder(0)]
    public ulong TraderNetworkId { get; set; }

    /// <summary>Stock slot index from <see cref="S2C.TraderStock"/>.</summary>
    [SerializeOrder(1)]
    public byte SlotIndex { get; set; }

    /// <summary>Units to buy (<c>1..999</c>).</summary>
    [SerializeOrder(2)]
    public ushort Quantity { get; set; } = 1;

    /// <summary>Restock epoch of the stock the client is looking at.</summary>
    [SerializeOrder(3)]
    public long Epoch { get; set; }
}
