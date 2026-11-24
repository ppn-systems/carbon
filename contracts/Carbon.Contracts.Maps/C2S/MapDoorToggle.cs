// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.C2S;

/// <summary>
/// Client opens or closes the door on a tile. Answered with <c>MutationResult</c>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapDoorToggle : PacketBase<MapDoorToggle>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MapOpCode.DoorToggle;

    /// <summary>World tile X.</summary>
    [SerializeOrder(0)]
    public int TileX { get; set; }

    /// <summary>World tile Y.</summary>
    [SerializeOrder(1)]
    public int TileY { get; set; }
}
