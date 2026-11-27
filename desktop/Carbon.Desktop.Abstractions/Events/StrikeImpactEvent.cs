// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;

namespace Carbon.Desktop.Abstractions.Events;

/// <summary>
/// A confirmed damaging hit (<c>CombatHit</c> with damage), buffered by the session for the impact presentation: hit-stop,
/// screen shake, target flash and particles (<c>docs/art/combat-animation.md</c> §3.4). Visual only; the server already
/// resolved the hit.
/// </summary>
/// <param name="AttackerNetworkId">Attacker's network id.</param>
/// <param name="DefenderNetworkId">Defender's network id.</param>
/// <param name="WorldPosition">Defender position when the hit arrived.</param>
/// <param name="Damage">Damage dealt.</param>
/// <param name="DefenderMaxHealth">Defender's max health (0 when unknown).</param>
/// <param name="IsCritical">Whether the hit was critical.</param>
/// <param name="IsKilled">Whether the hit killed the defender.</param>
/// <param name="IsLocalAttacker">Whether the local player dealt the hit.</param>
/// <param name="IsLocalVictim">Whether the local player took the hit.</param>
public readonly record struct StrikeImpactEvent(
    ulong AttackerNetworkId,
    ulong DefenderNetworkId,
    Vector2 WorldPosition,
    float Damage,
    float DefenderMaxHealth,
    bool IsCritical,
    bool IsKilled,
    bool IsLocalAttacker,
    bool IsLocalVictim);
