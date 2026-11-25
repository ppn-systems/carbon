// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Simulation.Clock;
using Carbon.Simulation.World;

namespace Carbon.Simulation.ECS.Systems;

/// <summary>
/// Execution stage inside a single fixed simulation tick.
/// </summary>
public enum SimulationPhase : byte
{
    /// <summary>Runs after command draining and before movement integration.</summary>
    PreUpdate = 0,

    /// <summary>Runs immediately after kinematic movement integration.</summary>
    Movement = 1,

    /// <summary>Runs immediately after entity-to-entity collision and trigger resolution.</summary>
    Physics = 2,

    /// <summary>Runs at the end of the fixed tick.</summary>
    PostUpdate = 3
}

/// <summary>
/// Contract for custom modular simulation systems registered on <see cref="WorldSimulation"/>.
/// </summary>
public interface ISimulationSystem
{
    /// <summary>
    /// Gets the fixed-tick execution phase in which this system runs.
    /// </summary>
    SimulationPhase Phase { get; }

    /// <summary>
    /// Executes the system logic for one deterministic fixed simulation tick.
    /// </summary>
    /// <param name="world">The owning world simulation.</param>
    /// <param name="tick">Contextual timing parameters for the current tick.</param>
    void Update(WorldSimulation world, FixedTickArgs tick);
}
