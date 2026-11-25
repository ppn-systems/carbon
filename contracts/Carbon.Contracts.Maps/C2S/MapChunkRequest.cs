// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.C2S;

/// <summary>
/// Sent by the client when a chunk enters its streaming radius.
/// Server responds with a stream of <see cref="S2C.MapChunkDelta"/> ending in a terminator,
/// or with only the terminator when <see cref="KnownRevision"/> is already current.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapChunkRequest : PacketBase<MapChunkRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MapOpCode.ChunkRequest;

    /// <summary>Chunk X coordinate.</summary>
    [SerializeOrder(0)]
    public int ChunkX { get; set; }

    /// <summary>Chunk Y coordinate.</summary>
    [SerializeOrder(1)]
    public int ChunkY { get; set; }

    /// <summary>Revision of the client's cached copy, or zero when it has none.</summary>
    [SerializeOrder(2)]
    public uint KnownRevision { get; set; }
}
