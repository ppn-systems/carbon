// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.ECS;

namespace Carbon.Combat.Attacks;

/// <summary>
/// Unmanaged per-entity attack state machine driven by <see cref="AttackSystem"/>. The default value is idle.
/// </summary>
/// <param name="Phase">Current timeline phase.</param>
/// <param name="Kind">Attack kind of the running profile.</param>
/// <param name="ProfileId">Index of the running <see cref="AttackProfile"/> in the owning <see cref="AttackSystem"/>.</param>
/// <param name="PhaseElapsedTicks">Ticks executed in the current phase (1-based once the phase has run).</param>
/// <param name="PhaseDurationTicks">Total ticks of the current phase.</param>
/// <param name="TotalElapsedTicks">Ticks executed since the attack started.</param>
/// <param name="TotalDurationTicks">Total ticks of the whole timeline.</param>
/// <param name="Target">Targeted entity (aim tracking for projectiles).</param>
/// <param name="Origin">Attacker position when the attack started.</param>
/// <param name="TargetPosition">Locked target point (spell telegraph center, leap landing point, aim point).</param>
/// <param name="Direction">Locked normalized facing direction.</param>
/// <param name="Damage">Damage dealt per struck target.</param>
/// <param name="TargetLayerMask">Collider layer mask of valid victims.</param>
/// <param name="Hits">Targets already struck by this attack.</param>
public readonly record struct AttackStateComponent(
    AttackPhase Phase,
    AttackKind Kind,
    ushort ProfileId,
    uint PhaseElapsedTicks,
    uint PhaseDurationTicks,
    uint TotalElapsedTicks,
    uint TotalDurationTicks,
    Entity Target,
    Vector2 Origin,
    Vector2 TargetPosition,
    Vector2 Direction,
    float Damage,
    uint TargetLayerMask,
    AttackHitList Hits)
{
    /// <summary>Gets a value indicating whether an attack is in progress.</summary>
    public bool IsBusy => this.Phase != AttackPhase.None;

    /// <summary>Gets the normalized progress <c>[0, 1]</c> through the current phase.</summary>
    public float PhaseProgress => this.PhaseDurationTicks == 0 ? 0f : (float)this.PhaseElapsedTicks / this.PhaseDurationTicks;

    /// <summary>Gets the normalized progress <c>[0, 1]</c> through the whole attack timeline.</summary>
    public float TotalProgress => this.TotalDurationTicks == 0 ? 0f : (float)this.TotalElapsedTicks / this.TotalDurationTicks;
}
