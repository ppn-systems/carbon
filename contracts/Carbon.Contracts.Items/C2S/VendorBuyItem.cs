// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client requests buying <see cref="Quantity"/> units of <see cref="ItemId"/> from a nearby NPC vendor.
/// Server responds with <see cref="Common.MutationResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class VendorBuyItem : PacketBase<VendorBuyItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.VendorBuyItem;

    /// <summary>Item definition identifier (must be vendor-buyable).</summary>
    [SerializeOrder(0)]
    public ushort ItemId { get; set; }

    /// <summary>Quantity to buy (<c>1..999</c>).</summary>
    [SerializeOrder(1)]
    public ushort Quantity { get; set; } = 1;
}
