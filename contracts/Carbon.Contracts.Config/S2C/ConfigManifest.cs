// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Config.S2C;

/// <summary>
/// Server-authoritative list of every config document, sent before world entry and pushed again whenever
/// a hot reload changes a document. Entries are parallel arrays indexed by document.
/// The client requests (<see cref="C2S.ConfigRequest"/>) every document whose cached hash differs.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ConfigManifest : PacketBase<ConfigManifest>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ConfigOpCode.Manifest;

    /// <summary>Monotonic manifest version; increases on every reload that changes a document.</summary>
    [SerializeOrder(0)]
    public uint ManifestVersion { get; set; }

    /// <summary>Document names (see <see cref="ConfigDocumentName"/>).</summary>
    [SerializeOrder(1)]
    public string[] Names { get; set; } = [];

    /// <summary>64-bit FNV-1a hash of each document's canonical UTF-8 bytes.</summary>
    [SerializeOrder(2)]
    public ulong[] ContentHashes { get; set; } = [];

    /// <summary>Canonical byte length of each document.</summary>
    [SerializeOrder(3)]
    public int[] ByteLengths { get; set; } = [];

    /// <summary>Per-document revision; increases each time that document's hash changes.</summary>
    [SerializeOrder(4)]
    public uint[] Versions { get; set; } = [];
}
