// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client asks to repair ONE item (ADR-028): <see cref="Steps"/> repair steps of the item in <see cref="SlotIndex"/>
/// at <see cref="Venue"/>. The server checks the venue, station tier, materials and gold from <c>repair.json</c>, spends
/// them atomically, and answers with <see cref="S2C.RepairItemResult"/>. The request carries no price or amount.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RepairItemRequest : PacketBase<RepairItemRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.RepairItemRequest;

    /// <summary>Inventory slot of the item (bag or equipped).</summary>
    [SerializeOrder(0)]
    public ushort SlotIndex { get; set; }

    /// <summary>Item id the client saw in the slot; a different item in the slot rejects the request.</summary>
    [SerializeOrder(1)]
    public ushort ExpectedItemId { get; set; }

    /// <summary>Repair steps to buy (<c>1..</c> the steps the item is missing). Fewer than needed is a partial repair.</summary>
    [SerializeOrder(2)]
    public byte Steps { get; set; }

    /// <summary>Where the repair happens: a player station or a blacksmith NPC.</summary>
    [SerializeOrder(3)]
    public RepairVenue Venue { get; set; }
}
