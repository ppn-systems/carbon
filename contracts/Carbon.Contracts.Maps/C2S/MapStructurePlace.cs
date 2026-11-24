// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.C2S;

/// <summary>
/// Client places a structure on a tile. Answered with <c>MutationResult</c>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapStructurePlace : PacketBase<MapStructurePlace>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MapOpCode.StructurePlace;

    /// <summary>World tile X.</summary>
    [SerializeOrder(0)]
    public int TileX { get; set; }

    /// <summary>World tile Y.</summary>
    [SerializeOrder(1)]
    public int TileY { get; set; }

    /// <summary>Structure type (<c>Carbon.Maps.Building.StructureType</c>).</summary>
    [SerializeOrder(2)]
    public byte StructureType { get; set; }
}
