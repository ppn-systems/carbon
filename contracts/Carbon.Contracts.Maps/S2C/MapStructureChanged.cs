// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.S2C;

/// <summary>
/// Pushed to every client whose area of interest contains the tile when a structure is placed, damaged,
/// toggled or removed. <see cref="StructureType"/> zero means the tile is now empty.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapStructureChanged : PacketBase<MapStructureChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MapOpCode.StructureChanged;

    /// <summary>World tile X.</summary>
    [SerializeOrder(0)]
    public int TileX { get; set; }

    /// <summary>World tile Y.</summary>
    [SerializeOrder(1)]
    public int TileY { get; set; }

    /// <summary>Structure type (<c>Carbon.Maps.Building.StructureType</c>), zero when removed.</summary>
    [SerializeOrder(2)]
    public byte StructureType { get; set; }

    // [SerializeOrder(3)] was Durability, never read; removed by ADR-038.

    /// <summary>Door state (<c>Carbon.Maps.Building.DoorState</c>).</summary>
    [SerializeOrder(4)]
    public byte DoorState { get; set; }

    // [SerializeOrder(5)] was Connectivity, never read; removed by ADR-038.
}
