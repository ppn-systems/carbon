// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.S2C;

/// <summary>
/// Pushed to every client whose area of interest contains the tile when a terrain tile changes.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapTileChanged : PacketBase<MapTileChanged>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MapOpCode.TileChanged;

    /// <summary>World tile X.</summary>
    [SerializeOrder(0)]
    public int TileX { get; set; }

    /// <summary>World tile Y.</summary>
    [SerializeOrder(1)]
    public int TileY { get; set; }

    /// <summary>Chunk revision after the change.</summary>
    [SerializeOrder(2)]
    public uint Revision { get; set; }

    /// <summary>New biome (<c>Carbon.Maps.Biomes.BiomeType</c>).</summary>
    [SerializeOrder(3)]
    public byte Biome { get; set; }

    /// <summary>New height level.</summary>
    [SerializeOrder(4)]
    public byte HeightLevel { get; set; }

    /// <summary>New visual variant.</summary>
    [SerializeOrder(5)]
    public byte Variant { get; set; }

    /// <summary>New tile traits.</summary>
    [SerializeOrder(6)]
    public MapTileTraits Traits { get; set; }
}
