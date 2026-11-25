// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Combat.Statuses;

namespace Carbon.Combat.Components;

/// <summary>
/// Unmanaged runtime combat state updated each tick from active status effects in <see cref="StatusEffectBuffer"/>.
/// </summary>
/// <param name="ControlMask">Combined crowd-control mask currently restricting the entity.</param>
/// <param name="MoveSpeedScale">Additive/multiplicative movement speed scale conferred by status effects (1.0 normal, 0.0 immobilized).</param>
public readonly record struct CombatStateComponent(
    CrowdControlMask ControlMask = CrowdControlMask.None,
    float MoveSpeedScale = 1f)
{
    /// <summary>
    /// Gets a value indicating whether incoming damage is ignored due to invulnerability.
    /// </summary>
    public bool IsInvulnerable => (this.ControlMask & CrowdControlMask.Invulnerable) != 0;

    /// <summary>
    /// Gets a value indicating whether the entity is allowed to move.
    /// </summary>
    public bool CanMove => (this.ControlMask & (CrowdControlMask.Stunned | CrowdControlMask.Rooted)) == 0;

    /// <summary>
    /// Gets a value indicating whether the entity is allowed to initiate basic actions.
    /// </summary>
    public bool CanAct => (this.ControlMask & CrowdControlMask.Stunned) == 0;

    /// <summary>
    /// Gets a value indicating whether the entity is allowed to cast abilities or spells.
    /// </summary>
    public bool CanCast => (this.ControlMask & (CrowdControlMask.Stunned | CrowdControlMask.Silenced)) == 0;
}
