// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.S2C;

/// <summary>
/// Combat damage notification pushed to clients observing either combatant.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class CombatHit : PacketBase<CombatHit>, IPacketStaticOpcode
{
    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.CombatHit;

    /// <summary>Attacker entity network ID.</summary>
    [SerializeOrder(0)]
    public ulong AttackerNetworkId { get; set; }

    /// <summary>Defender entity network ID.</summary>
    [SerializeOrder(1)]
    public ulong DefenderNetworkId { get; set; }

    /// <summary>Final authoritative damage dealt.</summary>
    [SerializeOrder(2)]
    public float Damage { get; set; }

    /// <summary>Defender remaining health after hit.</summary>
    [SerializeOrder(3)]
    public float RemainingHealth { get; set; }

    /// <summary>Whether the hit scored a critical strike.</summary>
    [SerializeOrder(4)]
    public bool IsCritical { get; set; }

    /// <summary>Whether the defender was killed by this hit.</summary>
    [SerializeOrder(5)]
    public bool IsKilled { get; set; }

    /// <summary>Status effect whose pulse dealt this damage (ADR-029 D9), 0 for a direct hit.</summary>
    [SerializeOrder(6)]
    public ushort StatusEffectId { get; set; }

    /// <summary>Level of the pulsing status effect (ADR-036), 0 for a direct hit.</summary>
    [SerializeOrder(7)]
    public byte StatusEffectLevel { get; set; }
}
