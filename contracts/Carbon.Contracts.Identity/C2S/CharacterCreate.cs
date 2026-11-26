// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Identity.C2S;

/// <summary>
/// Client request to create a new character on the authenticated account.
/// Server responds with <see cref="Common.MutationResult"/>.
/// </summary>
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

    /// <summary>Starter combat class discipline (<see cref="HeroArchetypeClass"/>).</summary>
    [SerializeOrder(2)]
    public byte StartingClass { get; set; }
}
