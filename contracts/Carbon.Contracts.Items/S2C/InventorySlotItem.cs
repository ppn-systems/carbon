// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// Streamed inventory slot item answered by <see cref="C2S.InventoryRequest"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class InventorySlotItem : PacketBase<InventorySlotItem>, IPacketStaticOpcode, IPacketStreamable, IPacketStreamTerminator
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.InventorySlotItem;

    /// <inheritdoc/>
    [SerializeOrder(0)]
    public bool IsEndOfStream { get; set; }

    /// <inheritdoc/>
    [SerializeOrder(1)]
    public bool IsTerminator { get; set; }

    /// <summary>Zero-based inventory slot index.</summary>
    [SerializeOrder(2)]
    public ushort SlotIndex { get; set; }

    /// <summary>Item archetype ID (0 when empty).</summary>
    [SerializeOrder(3)]
    public ushort ItemId { get; set; }

    /// <summary>Stack count.</summary>
    [SerializeOrder(4)]
    public int Count { get; set; }

    /// <summary>Remaining item durability.</summary>
    [SerializeOrder(5)]
    public float Durability { get; set; }

    /// <summary>Item enhancement level (<c>0..10</c>).</summary>
    [SerializeOrder(6)]
    public byte UpgradeLevel { get; set; }
}
