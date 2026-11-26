// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Config.S2C;

/// <summary>
/// One chunk of a config document in a stream response to <see cref="C2S.ConfigRequest"/>.
/// Documents larger than <see cref="ConfigProtocolLimits.ChunkBytes"/> are split into consecutive chunks;
/// the stream ends with a dedicated terminator packet. The client verifies the reassembled bytes against
/// <see cref="ContentHash"/> and the manifest before using or caching them.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class ConfigDocument : PacketBase<ConfigDocument>, IPacketStaticOpcode, IPacketStreamable, IPacketStreamTerminator
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => ConfigOpCode.Document;

    /// <inheritdoc/>
    [SerializeOrder(0)]
    public bool IsEndOfStream { get; set; }

    /// <inheritdoc/>
    [SerializeOrder(1)]
    public bool IsTerminator { get; set; }

    /// <summary>Document name.</summary>
    [SerializeOrder(2)]
    public string Name { get; set; } = string.Empty;

    /// <summary>64-bit FNV-1a hash of the whole document.</summary>
    [SerializeOrder(3)]
    public ulong ContentHash { get; set; }

    /// <summary>Document revision (matches the manifest entry).</summary>
    [SerializeOrder(4)]
    public uint Version { get; set; }

    /// <summary>Total byte length of the whole document.</summary>
    [SerializeOrder(5)]
    public int TotalLength { get; set; }

    /// <summary>Byte offset of <see cref="Payload"/> within the document.</summary>
    [SerializeOrder(6)]
    public int Offset { get; set; }

    /// <summary>Chunk bytes.</summary>
    [SerializeOrder(7)]
    public byte[] Payload { get; set; } = [];
}
