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
/// from logging in, and a trailing class byte from one would be ignored. Protocol 8 replaced the packed appearance byte
/// at order 1 with the structured look at orders 3–5 (ADR-042); the server validates it against <c>races.json</c>.
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

    // [SerializeOrder(1)] was the packed AppearancePreset byte, replaced by the structured look in protocol 8 (ADR-042).

    /// <summary>Race id of the new character (<c>races.json</c>, ADR-042).</summary>
    [SerializeOrder(3)]
    public ushort RaceId { get; set; }

    /// <summary>Body type id within the race (ADR-042).</summary>
    [SerializeOrder(4)]
    public byte BodyType { get; set; }

    /// <summary>One choice index per appearance option of the race, in the race's option order (ADR-042).</summary>
    [SerializeOrder(5)]
    public byte[] AppearanceOptions { get; set; } = [];
}
