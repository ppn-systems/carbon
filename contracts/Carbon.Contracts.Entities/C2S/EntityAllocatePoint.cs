// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.C2S;

/// <summary>
/// Client request to allocate unspent Attribute Points or unlock/upgrade Skills by numeric ID.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntityAllocatePoint : PacketBase<EntityAllocatePoint>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.AllocateCharacterPoint;

    /// <summary>Whether <see cref="TargetId"/> refers to an attribute or skill.</summary>
    [SerializeOrder(0)]
    public PointAllocationKind Kind { get; set; }

    /// <summary>Attribute (<see cref="PrimaryAttributeId"/>) or numeric skill identifier.</summary>
    [SerializeOrder(1)]
    public ushort TargetId { get; set; }

    /// <summary>Number of points to spend (clamped server-side).</summary>
    [SerializeOrder(2)]
    public byte Amount { get; set; } = 1;
}
