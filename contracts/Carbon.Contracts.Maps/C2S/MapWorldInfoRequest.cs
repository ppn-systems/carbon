// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Maps.C2S;

/// <summary>
/// Sent by the client after login to learn how to generate the world locally.
/// Server responds with <see cref="S2C.MapWorldInfo"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class MapWorldInfoRequest : PacketBase<MapWorldInfoRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => MapOpCode.WorldInfoRequest;
}
