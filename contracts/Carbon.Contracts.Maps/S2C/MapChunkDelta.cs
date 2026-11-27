// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.S2C;

/// <summary>
/// One chunk's edits in a stream response to <see cref="C2S.MapChunkRequest"/>. Tile, prop, and structure
/// changes are sent as parallel arrays indexed by local tile index (<c>localY * 32 + localX</c>).
/// A large delta may be split across several packets of the same chunk.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapChunkDelta : PacketBase<MapChunkDelta>, IPacketStaticOpcode, IPacketStreamable, IPacketStreamTerminator
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MapOpCode.ChunkDelta;

    /// <summary>Whether this is the last packet of the stream.</summary>
    [SerializeOrder(0)]
    public bool IsEndOfStream { get; set; }

    /// <summary>True only on the dedicated end-of-stream marker packet, never on a data item.</summary>
    [SerializeOrder(1)]
    public bool IsTerminator { get; set; }

    /// <summary>Chunk X coordinate.</summary>
    [SerializeOrder(2)]
    public int ChunkX { get; set; }

    /// <summary>Chunk Y coordinate.</summary>
    [SerializeOrder(3)]
    public int ChunkY { get; set; }

    /// <summary>Chunk revision after these edits.</summary>
    [SerializeOrder(4)]
    public uint Revision { get; set; }

    /// <summary>Local indices of edited tiles.</summary>
    [SerializeOrder(5)]
    public ushort[] TileIndices { get; set; } = [];

    /// <summary>Biome per edited tile (<c>Carbon.Maps.Biomes.BiomeType</c>).</summary>
    [SerializeOrder(6)]
    public byte[] TileBiomes { get; set; } = [];

    /// <summary>Height level per edited tile.</summary>
    [SerializeOrder(7)]
    public byte[] TileHeightLevels { get; set; } = [];

    /// <summary>Visual variant per edited tile.</summary>
    [SerializeOrder(8)]
    public byte[] TileVariants { get; set; } = [];

    /// <summary><see cref="MapTileTraits"/> per edited tile.</summary>
    [SerializeOrder(9)]
    public byte[] TileTraits { get; set; } = [];

    /// <summary>Local indices of the tiles whose prop changed.</summary>
    [SerializeOrder(10)]
    public ushort[] PropIndices { get; set; } = [];

    /// <summary>Remaining durability per changed prop.</summary>
    [SerializeOrder(11)]
    public float[] PropDurabilities { get; set; } = [];

    /// <summary>Whether each changed prop is destroyed.</summary>
    [SerializeOrder(12)]
    public bool[] PropDestroyed { get; set; } = [];

    /// <summary>Local indices of the tiles occupied by constructed structures.</summary>
    [SerializeOrder(13)]
    public ushort[] StructureIndices { get; set; } = [];

    /// <summary>Structure type (<c>Carbon.Maps.Building.StructureType</c>) per constructed structure.</summary>
    [SerializeOrder(14)]
    public byte[] StructureTypes { get; set; } = [];

    /// <summary>Remaining durability per constructed structure.</summary>
    [SerializeOrder(15)]
    public float[] StructureDurabilities { get; set; } = [];

    /// <summary>Door state (<c>Carbon.Maps.Building.DoorState</c>) per constructed structure.</summary>
    [SerializeOrder(16)]
    public byte[] StructureDoorStates { get; set; } = [];

    /// <summary>Wall connectivity mask (<c>Carbon.Maps.Building.WallConnectivityMask</c>) per constructed structure.</summary>
    [SerializeOrder(17)]
    public byte[] StructureConnectivities { get; set; } = [];

    /// <summary>Local anchor tile indices of construction sites (blueprints / frames).</summary>
    [SerializeOrder(18)]
    public ushort[] SiteIndices { get; set; } = [];

    /// <summary>Structure type being built per site (<c>Carbon.Maps.Building.StructureType</c>).</summary>
    [SerializeOrder(19)]
    public byte[] SiteTypes { get; set; } = [];

    /// <summary>Placement rotation per site (<c>Carbon.Maps.Building.StructureRotation</c>).</summary>
    [SerializeOrder(20)]
    public byte[] SiteRotations { get; set; } = [];

    /// <summary>Stage per site (<c>Carbon.Maps.Building.ConstructionStage</c>: 1 = Blueprint, 2 = Frame).</summary>
    [SerializeOrder(21)]
    public byte[] SiteStages { get; set; } = [];

    /// <summary>Rotated footprint per site, packed as <c>width | (height &lt;&lt; 4)</c>.</summary>
    [SerializeOrder(22)]
    public byte[] SiteSizes { get; set; } = [];

    /// <summary>Accumulated construction work points per site.</summary>
    [SerializeOrder(23)]
    public float[] SiteWorkDone { get; set; } = [];

    /// <summary>Owning player id per site.</summary>
    [SerializeOrder(24)]
    public ulong[] SiteOwnerIds { get; set; } = [];

    /// <summary>Local tile indices of completed-structure ownership records.</summary>
    [SerializeOrder(25)]
    public ushort[] RecordIndices { get; set; } = [];

    /// <summary>Owning player id per record.</summary>
    [SerializeOrder(26)]
    public ulong[] RecordOwnerIds { get; set; } = [];

    /// <summary>Offset from the structure anchor per record, packed as <c>dx | (dy &lt;&lt; 4)</c>.</summary>
    [SerializeOrder(27)]
    public byte[] RecordAnchorOffsets { get; set; } = [];

    /// <summary>Placement rotation per record (<c>Carbon.Maps.Building.StructureRotation</c>).</summary>
    [SerializeOrder(28)]
    public byte[] RecordRotations { get; set; } = [];

    /// <summary>Floor type covered by the structure per record (<c>Carbon.Maps.Building.StructureType</c>, 0 = none).</summary>
    [SerializeOrder(29)]
    public byte[] RecordFloorsBeneath { get; set; } = [];

    /// <summary>Tree growth stage per prop state, parallel to <see cref="PropIndices"/> (<c>Carbon.Maps.Props.TreeGrowthStage</c>, ADR-040).</summary>
    [SerializeOrder(30)]
    public byte[] PropGrowthStages { get; set; } = [];

    /// <summary>Local tile indices of trees players planted on tiles with no prop (ADR-040).</summary>
    [SerializeOrder(31)]
    public ushort[] PlantedIndices { get; set; } = [];

    /// <summary>Tree type per planted tree (<c>Carbon.Maps.Props.MapPropType</c>), parallel to <see cref="PlantedIndices"/>.</summary>
    [SerializeOrder(32)]
    public byte[] PlantedTypes { get; set; } = [];
}
