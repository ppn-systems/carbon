// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Identity.S2C;

/// <summary>
/// Streamed character roster item answered by <see cref="C2S.CharacterListRequest"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CharacterSummary : PacketBase<CharacterSummary>, IPacketStaticOpcode, IPacketStreamable, IPacketStreamTerminator
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => IdentityOpCode.CharacterSummary;

    /// <inheritdoc/>
    [SerializeOrder(0)]
    public bool IsEndOfStream { get; set; }

    /// <inheritdoc/>
    [SerializeOrder(1)]
    public bool IsTerminator { get; set; }

    /// <summary>Unique character identifier (<see cref="Guid.Empty"/> on the terminator frame).</summary>
    [SerializeOrder(2)]
    public Guid CharacterId { get; set; }

    /// <summary>Character display name.</summary>
    [SerializeOrder(3)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Character progression level.</summary>
    [SerializeOrder(4)]
    public ushort Level { get; set; }

    /// <summary>Current world zone progression tier.</summary>
    [SerializeOrder(5)]
    public byte ZoneTier { get; set; }

    /// <summary>Last saved world X position.</summary>
    [SerializeOrder(6)]
    public float PositionX { get; set; }

    /// <summary>Last saved world Y position.</summary>
    [SerializeOrder(7)]
    public float PositionY { get; set; }
}
