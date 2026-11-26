// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.C2S;

/// <summary>
/// Client request to start or stop resting (ADR-018). The server picks the mode: sleeping when a bed is within reach,
/// otherwise sitting. Answered with a <see cref="Common.MutationResult"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntityRestRequest : PacketBase<EntityRestRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.RestRequest;

    /// <summary><see langword="true"/> to rest (sit or sleep), <see langword="false"/> to stand up.</summary>
    [SerializeOrder(0)]
    public bool Rest { get; set; }
}
