// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client request to forge-enhance an item in <see cref="SlotIndex"/> (<c>+0..+10</c>). The server answers with
/// <see cref="S2C.EnhancementResult"/> (ADR-020); the client never chooses the outcome.
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

    /// <summary>
    /// Item id of the protection scroll to spend on this attempt (from <c>enhancement.json</c> <c>protections</c>),
    /// or <c>0</c> for none. The server takes it from the bag; it is only accepted where it can apply.
    /// </summary>
    [SerializeOrder(1)]
    public ushort ProtectionItemId { get; set; }
}
