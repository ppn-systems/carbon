// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Retired by ADR-028: a gold-only full repair. The server no longer handles it; clients send
/// <see cref="RepairItemRequest"/>. Kept until the desktop stops referencing it.
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
