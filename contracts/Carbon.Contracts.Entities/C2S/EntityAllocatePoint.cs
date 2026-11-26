// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.C2S;

/// <summary>
/// Client request to allocate unspent Attribute Points (<c>VIT/WIS/PWR/AGI</c>) or Skill Mastery Points (<c>Q/R/V</c>).
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntityAllocatePoint : PacketBase<EntityAllocatePoint>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.AllocateCharacterPoint;

    /// <summary>Attribute or active skill rank to increment.</summary>
    [SerializeOrder(0)]
    public CharacterPointTarget Target { get; set; }

    /// <summary>Number of points to spend (clamped server-side).</summary>
    [SerializeOrder(1)]
    public byte Amount { get; set; } = 1;
}
