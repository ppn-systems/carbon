// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client request to forge-enhance an item in <see cref="SlotIndex"/> (<c>+0..+10</c>).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class UpgradeInventoryItem : PacketBase<UpgradeInventoryItem>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.UpgradeInventoryItem;

    /// <summary>Target inventory slot index (<c>0..39</c>).</summary>
    [SerializeOrder(0)]
    public ushort SlotIndex { get; set; }
}
