// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// Unilateral server push updating one inventory slot on the owning client.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class InventorySlotChanged : PacketBase<InventorySlotChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.InventorySlotChanged;

    /// <summary>Zero-based inventory slot index.</summary>
    [SerializeOrder(0)]
    public ushort SlotIndex { get; set; }

    /// <summary>Item archetype ID (0 when cleared).</summary>
    [SerializeOrder(1)]
    public ushort ItemId { get; set; }

    /// <summary>Updated stack count.</summary>
    [SerializeOrder(2)]
    public int Count { get; set; }

    /// <summary>Updated item durability.</summary>
    [SerializeOrder(3)]
    public float Durability { get; set; }

    /// <summary>Updated item enhancement level (<c>0..10</c>).</summary>
    [SerializeOrder(4)]
    public byte UpgradeLevel { get; set; }
}
