// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.ECS;

namespace Carbon.AI.Components;

/// <summary>
/// Unmanaged ECS component holding autonomous NPC / monster state-machine configuration and runtime target state.
/// </summary>
/// <param name="State">Current finite-state machine state.</param>
/// <param name="HomePosition">Spawn / tether anchor position in world coordinates.</param>
/// <param name="AggroRadius">Target detection radius around current position.</param>
/// <param name="AttackRange">Range at which the actor stops chasing and enters <see cref="AiState.Attack"/>.</param>
/// <param name="LeashRadius">Maximum distance from <paramref name="HomePosition"/> before leashing back.</param>
/// <param name="MoveSpeed">Base movement speed in world units per second.</param>
/// <param name="TargetEntity">Currently pursued or attacked entity handle.</param>
/// <param name="TargetLayerMask">Collider layer mask used when scanning for valid targets.</param>
/// <param name="AttackCooldownTicks">Fixed ticks between attack ready events.</param>
/// <param name="CooldownRemainingTicks">Remaining ticks until next attack.</param>
/// <param name="PatrolRadius">Maximum patrol wander radius around <paramref name="HomePosition"/> when idle patrolling.</param>
/// <param name="PatrolDestination">Active patrol destination in world coordinates.</param>
/// <param name="PatrolWaitTicks">Remaining ticks to pause before selecting or moving to the next patrol waypoint.</param>
public readonly record struct AiBrainComponent(
    AiState State,
    Vector2 HomePosition,
    float AggroRadius,
    float AttackRange,
    float LeashRadius,
    float MoveSpeed,
    Entity TargetEntity = default,
    uint TargetLayerMask = uint.MaxValue,
    uint AttackCooldownTicks = 20,
    uint CooldownRemainingTicks = 0,
    float PatrolRadius = 0f,
    Vector2 PatrolDestination = default,
    uint PatrolWaitTicks = 0);
