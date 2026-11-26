// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.C2S;

/// <summary>
/// High-frequency movement input streamed from client to server per simulation tick.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntityMoveInput : PacketBase<EntityMoveInput>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.MoveInput;

    /// <summary>Client simulation tick used for prediction reconciliation.</summary>
    [SerializeOrder(0)]
    public uint ClientTick { get; set; }

    /// <summary>Normalized horizontal input direction [-1..1].</summary>
    [SerializeOrder(1)]
    public float DirectionX { get; set; }

    /// <summary>Normalized vertical input direction [-1..1].</summary>
    [SerializeOrder(2)]
    public float DirectionY { get; set; }

    /// <summary>True when sprint modifier is held.</summary>
    [SerializeOrder(3)]
    public bool Sprint { get; set; }
}
