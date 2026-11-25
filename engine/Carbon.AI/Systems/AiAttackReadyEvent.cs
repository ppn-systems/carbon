// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.ECS;

namespace Carbon.AI.Systems;

/// <summary>
/// Emitted by <see cref="AiBehaviorSystem"/> when an AI actor in <see cref="Components.AiState.Attack"/>
/// finishes its attack cooldown and is ready to strike its target.
/// </summary>
/// <param name="Attacker">The AI entity ready to attack.</param>
/// <param name="Target">The targeted entity.</param>
public readonly record struct AiAttackReadyEvent(Entity Attacker, Entity Target);
