// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client request to move, merge, or swap an inventory slot across hotbar, backpack, and armor slots.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MoveInventorySlot : PacketBase<MoveInventorySlot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.MoveInventorySlot;

    /// <summary>Source inventory slot index (<c>0..39</c>).</summary>
    [SerializeOrder(0)]
    public ushort FromSlot { get; set; }

    /// <summary>Destination inventory slot index (<c>0..39</c>).</summary>
    [SerializeOrder(1)]
    public ushort ToSlot { get; set; }
}
