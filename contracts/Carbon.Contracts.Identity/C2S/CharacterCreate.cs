// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Identity.C2S;

/// <summary>
/// Client request to create a new character on the authenticated account: a name and a look, nothing else.
/// Characters start classless with an empty bag (ADR-030). Server responds with <see cref="Common.MutationResult"/>.
/// </summary>
/// <remarks>
/// Protocol 2 removed the protocol 1 <c>StartingClass</c> byte at order 2; the version bump keeps protocol 1 clients
/// from logging in, and a trailing class byte from one would be ignored.
/// </remarks>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CharacterCreate : PacketBase<CharacterCreate>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => IdentityOpCode.CharacterCreate;

    /// <summary>Display name of the new character.</summary>
    [SerializeOrder(0)]
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>Visual preset identifier.</summary>
    [SerializeOrder(1)]
    public byte AppearancePreset { get; set; }
}
