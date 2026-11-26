// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.ECS;

namespace Carbon.Combat.Attacks;

/// <summary>
/// Published by <see cref="AttackSystem"/> on the first tick of every attack phase (and with
/// <see cref="AttackPhase.None"/> when an attack finishes or is interrupted). This is the animation contract for
/// renderers: clients play the animation for (<see cref="Kind"/>, <see cref="Phase"/>) and advance it locally over
/// <see cref="PhaseDurationTicks"/>. Damage is never resolved on the client.
/// </summary>
/// <param name="Attacker">Attacking entity.</param>
/// <param name="Kind">Attack kind.</param>
/// <param name="Phase">Phase that starts on this tick.</param>
/// <param name="ProfileId">Attack profile index.</param>
/// <param name="Origin">Attacker position when the attack started.</param>
/// <param name="TargetPosition">Locked target point (telegraph center, landing point, or current aim point).</param>
/// <param name="Direction">Normalized facing direction.</param>
/// <param name="AreaRadius">Telegraph / reach radius.</param>
/// <param name="HalfAngleRadians">Telegraph cone half-angle (melee arcs and cone spells).</param>
/// <param name="Shape">Telegraph geometry.</param>
/// <param name="PhaseDurationTicks">Duration of the phase that starts on this tick.</param>
/// <param name="Progress">Normalized progress <c>[0, 1]</c> through the whole attack timeline at the phase start.</param>
/// <param name="Interrupted">Whether a <see cref="AttackPhase.None"/> event was caused by cancellation.</param>
public readonly record struct AttackPhaseChangedEvent(
    Entity Attacker,
    AttackKind Kind,
    AttackPhase Phase,
    ushort ProfileId,
    Vector2 Origin,
    Vector2 TargetPosition,
    Vector2 Direction,
    float AreaRadius,
    float HalfAngleRadians,
    AttackAreaShape Shape,
    uint PhaseDurationTicks,
    float Progress,
    bool Interrupted);
