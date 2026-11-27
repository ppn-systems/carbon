// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.S2C;

/// <summary>
/// Per-tick batched delta snapshot carrying the receiving player's authoritative state and
/// changed visible entities in Area of Interest using structure-of-arrays parallel fields.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntityDeltaBatch : PacketBase<EntityDeltaBatch>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.EntityDeltaBatch;

    /// <summary>Authoritative server tick.</summary>
    [SerializeOrder(0)]
    public ulong ServerTick { get; set; }

    /// <summary>Latest client input tick incorporated by the server.</summary>
    [SerializeOrder(1)]
    public uint LastProcessedClientTick { get; set; }

    /// <summary>Authoritative X position of the receiving player's entity.</summary>
    [SerializeOrder(2)]
    public float SelfPositionX { get; set; }

    /// <summary>Authoritative Y position of the receiving player's entity.</summary>
    [SerializeOrder(3)]
    public float SelfPositionY { get; set; }

    /// <summary>Visible entity network IDs updated in this batch.</summary>
    [SerializeOrder(4)]
    public ulong[] NetworkIds { get; set; } = [];

    // [SerializeOrder(5)] was DirtyMasks: every column is always sent in full, so the client never read it; removed by
    // ADR-038.

    /// <summary>Updated world X positions per entity.</summary>
    [SerializeOrder(6)]
    public float[] PositionsX { get; set; } = [];

    /// <summary>Updated world Y positions per entity.</summary>
    [SerializeOrder(7)]
    public float[] PositionsY { get; set; } = [];

    /// <summary>Updated X velocities per entity.</summary>
    [SerializeOrder(8)]
    public float[] VelocitiesX { get; set; } = [];

    /// <summary>Updated Y velocities per entity.</summary>
    [SerializeOrder(9)]
    public float[] VelocitiesY { get; set; } = [];

    /// <summary>Updated health values per entity.</summary>
    [SerializeOrder(10)]
    public float[] CurrentHealths { get; set; } = [];
}
