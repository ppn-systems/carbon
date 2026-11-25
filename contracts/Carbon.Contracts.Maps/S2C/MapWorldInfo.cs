// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.S2C;

/// <summary>
/// Answer to <see cref="C2S.MapWorldInfoRequest"/>. The client generates every chunk locally from
/// <see cref="Seed"/> and only downloads the edits (<see cref="MapChunkDelta"/>).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapWorldInfo : PacketBase<MapWorldInfo>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MapOpCode.WorldInfo;

    /// <summary>World generation seed.</summary>
    [SerializeOrder(0)]
    public ulong Seed { get; set; }

    /// <summary>Generator version. A client whose generator differs must refuse to play rather than desync.</summary>
    [SerializeOrder(1)]
    public ushort GeneratorVersion { get; set; }

    /// <summary>Tile size in world units.</summary>
    [SerializeOrder(2)]
    public float TileSize { get; set; }

    /// <summary>Base noise frequency.</summary>
    [SerializeOrder(3)]
    public float Frequency { get; set; }

    /// <summary>Spawn tile X.</summary>
    [SerializeOrder(4)]
    public int SpawnTileX { get; set; }

    /// <summary>Spawn tile Y.</summary>
    [SerializeOrder(5)]
    public int SpawnTileY { get; set; }
}
