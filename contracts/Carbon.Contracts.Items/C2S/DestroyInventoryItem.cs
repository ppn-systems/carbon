// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client request to destroy items in a slot permanently, after the player confirmed it (ADR-020).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class DestroyInventoryItem : PacketBase<DestroyInventoryItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.DestroyInventoryItem;

    /// <summary>Inventory slot index (<c>0..41</c>).</summary>
    [SerializeOrder(0)]
    public ushort SlotIndex { get; set; }

    /// <summary>Quantity to destroy (&lt;= 0 destroys the entire slot).</summary>
    [SerializeOrder(1)]
    public int Quantity { get; set; }

    /// <summary>Item the client expects in the source slot; the server answers <c>Conflict</c> when it differs (ADR-020 D2).</summary>
    [SerializeOrder(2)]
    public ushort ExpectedItemId { get; set; }
}
