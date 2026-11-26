// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Config.C2S;

/// <summary>
/// Sent by the client after a <see cref="S2C.ConfigManifest"/> for every document that is missing from its
/// cache or whose cached hash differs. The server answers with a stream of <see cref="S2C.ConfigDocument"/>
/// chunks ending in a terminator.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ConfigRequest : PacketBase<ConfigRequest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ConfigOpCode.Request;

    /// <summary>Manifest version the client is syncing against.</summary>
    [SerializeOrder(0)]
    public uint ManifestVersion { get; set; }

    /// <summary>Names of the requested documents (at most <see cref="ConfigProtocolLimits.MaxDocuments"/>).</summary>
    [SerializeOrder(1)]
    public string[] Names { get; set; } = [];
}
