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

    /// <summary>Source inventory slot index (<c>0..41</c>).</summary>
    [SerializeOrder(0)]
    public ushort SlotIndex { get; set; }

    /// <summary>Quantity to drop (&lt;= 0 drops the entire slot).</summary>
    [SerializeOrder(1)]
    public int Quantity { get; set; }

    /// <summary>
    /// Item the client expects in the source slot. The server answers <c>Conflict</c> when it differs, so a stale or
    /// replayed command never acts on another item; <c>0</c> skips the check (ADR-020 D2).
    /// </summary>
    [SerializeOrder(2)]
    public ushort ExpectedItemId { get; set; }
}
