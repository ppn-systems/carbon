// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client requests restoring full durability of the item in <see cref="SlotIndex"/> for gold at a forge station.
/// Server responds with <see cref="Common.MutationResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RepairInventoryItem : PacketBase<RepairInventoryItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.RepairInventoryItem;

    /// <summary>Inventory slot index (<c>0..41</c>, equipped armor included).</summary>
    [SerializeOrder(0)]
    public ushort SlotIndex { get; set; }
}
