// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client requests selling <see cref="Quantity"/> units from bag slot <see cref="SlotIndex"/> to a nearby NPC vendor.
/// Server responds with <see cref="Common.MutationResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class VendorSellItem : PacketBase<VendorSellItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.VendorSellItem;

    /// <summary>Bag slot index (<c>0..35</c>; equipped armor cannot be sold).</summary>
    [SerializeOrder(0)]
    public ushort SlotIndex { get; set; }

    /// <summary>Quantity to sell from the slot.</summary>
    [SerializeOrder(1)]
    public ushort Quantity { get; set; } = 1;
}
