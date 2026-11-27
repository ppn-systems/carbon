// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.AI.Components;

/// <summary>
/// Optional, unmanaged per-actor tactics configuration read by <see cref="Systems.AiBehaviorSystem"/>. Actors without
/// this component keep the plain <c>Idle / Patrol -> Chase -> Attack -> ReturnHome</c> behaviour. All values are static
/// for the actor's lifetime (runtime counters live in <see cref="AiBrainComponent"/>).
/// </summary>
/// <param name="PackId">Social group id; actors with the same non-zero id assist each other. 0 = solitary.</param>
/// <param name="AssistRadius">Radius in which pack allies answer a call for help (aggro, flee, or being hit).</param>
/// <param name="FleeHealthFraction">Health fraction (0..1) at or below which the actor flees once per engagement; 0 disables fleeing.</param>
/// <param name="FleeTicks">Duration of a flee episode in ticks.</param>
/// <param name="GiveUpTicks">Consecutive chase ticks without reaching attack range before giving up and leashing home; 0 disables.</param>
/// <param name="ThreatSwitchMargin">
/// Target-switch hysteresis: a new threat leader replaces the current target only when its threat exceeds the current
/// target's threat times this factor (e.g. 1.1 = 10% more). Values below 1 are treated as 1.
/// </param>
/// <param name="SurroundRadius">
/// Melee ring radius around the target. When positive, chasers steer to distinct slots on this ring (flanking) instead
/// of the target center; 0 disables surround steering.
/// </param>
/// <param name="NightAggroMultiplier">Aggro and assist radius multiplier while <see cref="Systems.AiBehaviorSystem.IsNight"/>.</param>
/// <param name="NightLeashMultiplier">Leash radius multiplier while <see cref="Systems.AiBehaviorSystem.IsNight"/>.</param>
public readonly record struct AiTacticsComponent(
    ushort PackId = 0,
    float AssistRadius = 0f,
    float FleeHealthFraction = 0f,
    uint FleeTicks = 0,
    uint GiveUpTicks = 0,
    float ThreatSwitchMargin = 1f,
    float SurroundRadius = 0f,
    float NightAggroMultiplier = 1f,
    float NightLeashMultiplier = 1f);
