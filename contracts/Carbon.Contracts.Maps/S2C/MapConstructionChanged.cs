// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.S2C;

/// <summary>
/// Pushed to every client whose area of interest contains the anchor tile when a construction site is placed,
/// changes stage or progresses, or when a structure is being deconstructed. The server throttles progress pushes
/// to coarse steps; clients only render this, every mutation stays server-authoritative.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapConstructionChanged : PacketBase<MapConstructionChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MapOpCode.ConstructionChanged;

    /// <summary>Anchor (top-left) world tile X of the footprint.</summary>
    [SerializeOrder(0)]
    public int TileX { get; set; }

    /// <summary>Anchor (top-left) world tile Y of the footprint.</summary>
    [SerializeOrder(1)]
    public int TileY { get; set; }

    /// <summary>Structure type (<c>Carbon.Maps.Building.StructureType</c>).</summary>
    [SerializeOrder(2)]
    public byte StructureType { get; set; }

    /// <summary>Placement rotation (<c>Carbon.Maps.Building.StructureRotation</c>).</summary>
    [SerializeOrder(3)]
    public byte Rotation { get; set; }

    /// <summary>
    /// Stage (<c>Carbon.Maps.Building.ConstructionStage</c>): 1 = Blueprint, 2 = Frame, 3 = Complete (being deconstructed),
    /// 4 = Removed (site finished / cancelled, or deconstruction stopped or finished).
    /// </summary>
    [SerializeOrder(4)]
    public byte Stage { get; set; }

    /// <summary>Rotated footprint packed as <c>width | (height &lt;&lt; 4)</c>.</summary>
    [SerializeOrder(5)]
    public byte Size { get; set; }

    /// <summary>Construction or deconstruction progress in [0, 1].</summary>
    [SerializeOrder(6)]
    public float Progress { get; set; }

    /// <summary>Whether <see cref="Progress"/> is deconstruction of a completed structure.</summary>
    [SerializeOrder(7)]
    public bool IsDeconstruction { get; set; }
}
