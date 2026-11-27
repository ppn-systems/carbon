// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.S2C;

/// <summary>
/// Broadcasts active ability cast VFX origin and aim direction to nearby observers.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class SkillCastBroadcast : PacketBase<SkillCastBroadcast>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.SkillCastBroadcast;

    /// <summary>Network entity ID of the casting actor.</summary>
    [SerializeOrder(0)]
    public ulong CasterNetworkId { get; set; }

    /// <summary>Activated skill slot (<c>1..3</c>).</summary>
    [SerializeOrder(1)]
    public byte AbilitySlot { get; set; }

    // [SerializeOrder(2)] was OriginX, never read; removed by ADR-038.

    // [SerializeOrder(3)] was OriginY, never read; removed by ADR-038.

    /// <summary>Target world aim X coordinate.</summary>
    [SerializeOrder(4)]
    public float AimX { get; set; }

    /// <summary>Target world aim Y coordinate.</summary>
    [SerializeOrder(5)]
    public float AimY { get; set; }
}
