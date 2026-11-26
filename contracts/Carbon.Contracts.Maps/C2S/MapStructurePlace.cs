// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.C2S;

/// <summary>
/// Client places a structure blueprint anchored on a tile. Answered with <c>MutationResult</c>; the server then
/// consumes materials and construction work as the player builds (see <c>docs/building.md</c>).
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

    /// <summary>Placement rotation (<c>Carbon.Maps.Building.StructureRotation</c>: 0 = North, 1 = East, 2 = South, 3 = West).</summary>
    [SerializeOrder(3)]
    public byte Rotation { get; set; }
}
