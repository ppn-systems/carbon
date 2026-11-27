// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Contracts.Common;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.S2C;

/// <summary>
/// Server reply to <see cref="C2S.RepairItemRequest"/> (ADR-028). The repaired slot, the spent materials and the wallet
/// arrive first as <see cref="InventorySlotChanged"/> and the wallet sync. On failure nothing was spent.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class RepairItemResult : PacketBase<RepairItemResult>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.RepairItemResult;

    /// <summary><see cref="OpStatus.Ok"/> when the repair ran, otherwise the queue or world status.</summary>
    [SerializeOrder(0)]
    public OpStatus Status { get; set; }

    /// <summary>Why the repair was refused (<see cref="RepairError.None"/> on success).</summary>
    [SerializeOrder(1)]
    public RepairError Error { get; set; }

    /// <summary>The slot the request named.</summary>
    [SerializeOrder(2)]
    public ushort SlotIndex { get; set; }

    /// <summary>Steps that were bought (0 on failure).</summary>
    [SerializeOrder(3)]
    public byte Steps { get; set; }
}
