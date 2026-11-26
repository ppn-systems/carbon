// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Contracts.Entities.S2C;

namespace Carbon.Desktop.Abstractions.World;

/// <summary>
/// Render-side view of the server-authoritative attack phase most recently announced by <see cref="EntityAttackPhase"/>,
/// advanced locally between packets. Purely presentational: the client never resolves hits from it.
/// </summary>
/// <param name="AttackKind">Attack kind byte (<see cref="EntityAttackPhase.KindMelee"/> .. <see cref="EntityAttackPhase.KindLeap"/>).</param>
/// <param name="Phase">Phase byte (<see cref="EntityAttackPhase.PhaseNone"/> .. <see cref="EntityAttackPhase.PhaseRecovery"/>).</param>
/// <param name="PhaseProgress">Locally advanced progress <c>[0, 1]</c> through the current phase.</param>
/// <param name="Target">Locked target / aim / landing point.</param>
/// <param name="Direction">Normalized facing direction.</param>
/// <param name="AreaRadius">Telegraph radius.</param>
/// <param name="HalfAngleRadians">Telegraph cone half-angle.</param>
/// <param name="Shape">Telegraph shape (<c>0</c> circle, <c>1</c> cone).</param>
public readonly record struct AttackPhaseView(
    byte AttackKind,
    byte Phase,
    float PhaseProgress,
    Vector2 Target,
    Vector2 Direction,
    float AreaRadius,
    float HalfAngleRadians,
    byte Shape)
{
    /// <summary>Gets a value indicating whether an attack is in progress.</summary>
    public bool IsActive => this.Phase != EntityAttackPhase.PhaseNone;
}
