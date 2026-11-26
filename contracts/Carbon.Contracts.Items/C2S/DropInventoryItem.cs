// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client request to drop an item stack from <see cref="SlotIndex"/> onto the ground as loot.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DropInventoryItem : PacketBase<DropInventoryItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.DropInventoryItem;

    /// <summary>Source inventory slot index (<c>0..39</c>).</summary>
    [SerializeOrder(0)]
    public ushort SlotIndex { get; set; }

    /// <summary>Quantity to drop (&lt;= 0 drops the entire slot).</summary>
    [SerializeOrder(1)]
    public int Quantity { get; set; }
}
