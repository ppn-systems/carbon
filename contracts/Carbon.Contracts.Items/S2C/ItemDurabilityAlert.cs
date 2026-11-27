// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// Server push to the owner of a worn item (ADR-035): the item is about to break, or it broke and was destroyed. The
/// matching <see cref="InventorySlotChanged"/> carries the slot itself. Ids only: the client names the item and
/// localizes the text.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ItemDurabilityAlert : PacketBase<ItemDurabilityAlert>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.ItemDurabilityAlert;

    /// <summary>What happened to the item.</summary>
    [SerializeOrder(0)]
    public DurabilityAlertKind Kind { get; set; }

    /// <summary>Inventory slot the item is (or was) in.</summary>
    [SerializeOrder(1)]
    public ushort SlotIndex { get; set; }

    /// <summary>Item archetype id.</summary>
    [SerializeOrder(2)]
    public ushort ItemId { get; set; }

    /// <summary>The item's enhancement level (<c>0..10</c>).</summary>
    [SerializeOrder(3)]
    public byte UpgradeLevel { get; set; }
}
