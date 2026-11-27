// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client request to move part of a bag stack into an empty bag slot or onto a matching stack (ADR-020).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SplitInventorySlot : PacketBase<SplitInventorySlot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.SplitInventorySlot;

    /// <summary>Source bag slot index (<c>0..35</c>).</summary>
    [SerializeOrder(0)]
    public ushort FromSlot { get; set; }

    /// <summary>Destination bag slot index (<c>0..35</c>), empty or holding the same stackable item.</summary>
    [SerializeOrder(1)]
    public ushort ToSlot { get; set; }

    /// <summary>Units to move (<c>1..stack-1</c>).</summary>
    [SerializeOrder(2)]
    public ushort Quantity { get; set; }

    /// <summary>Item the client expects in the source slot; the server answers <c>Conflict</c> when it differs (ADR-020 D2).</summary>
    [SerializeOrder(3)]
    public ushort ExpectedItemId { get; set; }
}
