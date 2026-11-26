// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.S2C;

/// <summary>
/// Pushed to a client when a previously visible entity exits its Area of Interest or despawns.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntityDespawn : PacketBase<EntityDespawn>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.EntityDespawn;

    /// <summary>Network entity identifier that disappeared.</summary>
    [SerializeOrder(0)]
    public ulong NetworkId { get; set; }

    /// <summary>Server simulation tick at despawn.</summary>
    [SerializeOrder(1)]
    public ulong ServerTick { get; set; }
}
