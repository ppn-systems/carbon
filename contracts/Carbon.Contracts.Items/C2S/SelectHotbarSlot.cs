// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Items.C2S;

/// <summary>
/// Client selects an active hotbar slot index (0..9). Server responds with <see cref="Common.MutationResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SelectHotbarSlot : PacketBase<SelectHotbarSlot>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ItemOpCode.SelectHotbarSlot;

    /// <summary>Hotbar slot index [0..9].</summary>
    [SerializeOrder(0)]
    public byte SlotIndex { get; set; }
}
