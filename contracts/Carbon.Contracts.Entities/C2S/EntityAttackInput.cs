// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.C2S;

/// <summary>
/// Client request to attack a target or cast an ability slot toward an aim point.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntityAttackInput : PacketBase<EntityAttackInput>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.AttackInput;

    /// <summary>Client simulation tick.</summary>
    [SerializeOrder(0)]
    public uint ClientTick { get; set; }

    /// <summary>Optional target network entity identifier, or zero for positional swing.</summary>
    [SerializeOrder(1)]
    public ulong TargetNetworkId { get; set; }

    /// <summary>Aim target X coordinate in world units.</summary>
    [SerializeOrder(2)]
    public float AimX { get; set; }

    /// <summary>Aim target Y coordinate in world units.</summary>
    [SerializeOrder(3)]
    public float AimY { get; set; }

    /// <summary>Hotbar or ability slot index.</summary>
    [SerializeOrder(4)]
    public byte AbilitySlot { get; set; }
}
