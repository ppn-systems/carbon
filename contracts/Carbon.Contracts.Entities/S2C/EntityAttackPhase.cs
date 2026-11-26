// Copyright (c) 2026 PPN Systems. All rights reserved.

using Nalix.Abstractions.Networking.Packets;
using Nalix.Abstractions.Serialization;
using Nalix.Codec.DataFrames;

namespace Carbon.Contracts.Entities.S2C;

/// <summary>
/// Announces that an entity's server-authoritative attack entered a new phase. Clients animate from it and draw
/// telegraphs; they never resolve damage (hits arrive as <see cref="CombatHit"/>). One packet is sent per phase start,
/// not per tick: clients advance the phase locally over <see cref="PhaseDurationTicks"/>.
/// See <c>docs/adr/ADR-013-entity-attack-phase-packet.md</c>.
/// </summary>
[Packet]
[GenerateFormatter]
[SerializePackable(SerializeLayout.Explicit)]
public sealed partial class EntityAttackPhase : PacketBase<EntityAttackPhase>, IPacketStaticOpcode
{
    /// <summary>Attack kind: <c>0</c> melee, <c>1</c> projectile, <c>2</c> spell, <c>3</c> leap.</summary>
    public const byte KindMelee = 0;

    /// <summary>Projectile attack kind.</summary>
    public const byte KindProjectile = 1;

    /// <summary>Spell attack kind.</summary>
    public const byte KindSpell = 2;

    /// <summary>Leap / charge attack kind.</summary>
    public const byte KindLeap = 3;

    /// <summary>Phase: attack finished or interrupted.</summary>
    public const byte PhaseNone = 0;

    /// <summary>Phase: wind-up telegraph / aim / cast channel / crouch.</summary>
    public const byte PhaseWindup = 1;

    /// <summary>Phase: lunge / delayed detonation / airborne.</summary>
    public const byte PhaseTravel = 2;

    /// <summary>Phase: hit frames / release / detonation / landing.</summary>
    public const byte PhaseActive = 3;

    /// <summary>Phase: recovery.</summary>
    public const byte PhaseRecovery = 4;

    /// <inheritdoc/>
    public static ushort StaticOpCode => EntityOpCode.EntityAttackPhase;

    /// <summary>Network entity ID of the attacker.</summary>
    [SerializeOrder(0)]
    public ulong NetworkId { get; set; }

    /// <summary>Server tick on which the phase started.</summary>
    [SerializeOrder(1)]
    public ulong ServerTick { get; set; }

    /// <summary>Attack kind (<see cref="KindMelee"/> .. <see cref="KindLeap"/>).</summary>
    [SerializeOrder(2)]
    public byte AttackKind { get; set; }

    /// <summary>Phase that starts (<see cref="PhaseNone"/> .. <see cref="PhaseRecovery"/>).</summary>
    [SerializeOrder(3)]
    public byte Phase { get; set; }

    /// <summary>Telegraph shape: <c>0</c> circle, <c>1</c> cone.</summary>
    [SerializeOrder(4)]
    public byte Shape { get; set; }

    /// <summary>Whether a <see cref="PhaseNone"/> packet reports an interruption (stun, leash) rather than completion.</summary>
    [SerializeOrder(5)]
    public bool Interrupted { get; set; }

    /// <summary>Locked target / aim / landing point X.</summary>
    [SerializeOrder(6)]
    public float TargetX { get; set; }

    /// <summary>Locked target / aim / landing point Y.</summary>
    [SerializeOrder(7)]
    public float TargetY { get; set; }

    /// <summary>Normalized facing direction X.</summary>
    [SerializeOrder(8)]
    public float DirectionX { get; set; }

    /// <summary>Normalized facing direction Y.</summary>
    [SerializeOrder(9)]
    public float DirectionY { get; set; }

    /// <summary>Telegraph / reach radius in world units.</summary>
    [SerializeOrder(10)]
    public float AreaRadius { get; set; }

    /// <summary>Cone / arc half-angle in radians.</summary>
    [SerializeOrder(11)]
    public float HalfAngleRadians { get; set; }

    /// <summary>Duration of the phase in server ticks.</summary>
    [SerializeOrder(12)]
    public ushort PhaseDurationTicks { get; set; }

    /// <summary>Normalized progress <c>[0, 1]</c> through the whole attack when this phase starts.</summary>
    [SerializeOrder(13)]
    public float Progress { get; set; }
}
