// Copyright (c) 2026 PPN Systems. All rights reserved.

namespace Carbon.AI.Components;

/// <summary>
/// Finite-state machine states for autonomous NPC / creature behavior.
/// </summary>
public enum AiState : byte
{
    /// <summary>Standing idle near home position scanning for targets.</summary>
    Idle = 0,

    /// <summary>Patrolling around home anchor.</summary>
    Patrol = 1,

    /// <summary>Pursuing an active target.</summary>
    Chase = 2,

    /// <summary>In melee/attack range executing cooldown-gated attacks.</summary>
    Attack = 3,

    /// <summary>Leashed back to home anchor after leaving tether radius or losing target.</summary>
    ReturnHome = 4
}
