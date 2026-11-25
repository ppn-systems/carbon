// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Identity.C2S;

/// <summary>
/// Client request to enter the authoritative world simulation with the specified character.
/// Server responds with <see cref="S2C.CharacterEnterWorld"/>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CharacterSelect : PacketBase<CharacterSelect>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => IdentityOpCode.CharacterSelect;

    /// <summary>Identifier of the character to spawn.</summary>
    [SerializeOrder(0)]
    public Guid CharacterId { get; set; }
}
