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

    /// <summary>Source inventory slot index (<c>0..41</c>).</summary>
    [SerializeOrder(0)]
    public ushort FromSlot { get; set; }

    /// <summary>Destination inventory slot index (<c>0..41</c>).</summary>
    [SerializeOrder(1)]
    public ushort ToSlot { get; set; }

    /// <summary>
    /// Item the client expects in the source slot. The server answers <c>Conflict</c> when it differs, so a stale or
    /// replayed command never acts on another item; <c>0</c> skips the check (ADR-019 D2).
    /// </summary>
    [SerializeOrder(2)]
    public ushort ExpectedItemId { get; set; }
}
