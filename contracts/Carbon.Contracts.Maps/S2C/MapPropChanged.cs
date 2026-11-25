// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.S2C;

/// <summary>
/// Pushed to every client whose area of interest contains the tile when a natural prop is damaged or destroyed.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapPropChanged : PacketBase<MapPropChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MapOpCode.PropChanged;

    /// <summary>World tile X.</summary>
    [SerializeOrder(0)]
    public int TileX { get; set; }

    /// <summary>World tile Y.</summary>
    [SerializeOrder(1)]
    public int TileY { get; set; }

    /// <summary>Chunk revision after the change.</summary>
    [SerializeOrder(2)]
    public uint Revision { get; set; }

    /// <summary>Remaining durability.</summary>
    [SerializeOrder(3)]
    public float Durability { get; set; }

    /// <summary>Whether the prop is destroyed.</summary>
    [SerializeOrder(4)]
    public bool IsDestroyed { get; set; }
}
