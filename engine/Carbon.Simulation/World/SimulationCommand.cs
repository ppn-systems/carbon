// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.ECS;

namespace Carbon.Simulation.World;

/// <summary>
/// Identifies the operation carried by a <see cref="SimulationCommand"/> queued for execution on the next fixed tick.
/// </summary>
public enum SimulationCommandKind : byte
{
    /// <summary>No operation.</summary>
    None = 0,

    /// <summary>Applies directional movement input scaled by terrain speed multiplier.</summary>
    SetMoveInput = 1,

    /// <summary>Directly assigns an entity's linear velocity vector.</summary>
    SetVelocity = 2,

    /// <summary>Teleports an entity directly to a target world position and updates spatial partitioning.</summary>
    Teleport = 3,

    /// <summary>Despawns an entity from the simulation world.</summary>
    Despawn = 4
}

/// <summary>
/// Thread-safe value command enqueued from network handlers or input threads and consumed sequentially on the
/// authoritative <see cref="WorldSimulation"/> tick thread.
/// </summary>
/// <param name="Kind">The command type.</param>
/// <param name="Entity">The target entity handle.</param>
/// <param name="Vector">Direction, linear velocity, or target position depending on <see cref="Kind"/>.</param>
/// <param name="BaseSpeed">Base movement speed in world units per second (used by <see cref="SimulationCommandKind.SetMoveInput"/>).</param>
/// <param name="ClientTick">Optional client tick number associated with the command.</param>
public readonly record struct SimulationCommand(
    SimulationCommandKind Kind,
    Entity Entity,
    Vector2 Vector,
    float BaseSpeed = 0f,
    uint ClientTick = 0)
{
    /// <summary>Creates a directional movement input command scaled by terrain speed during tick processing.</summary>
    public static SimulationCommand MoveInput(Entity entity, Vector2 direction, float baseSpeed, uint clientTick = 0) =>
        new(SimulationCommandKind.SetMoveInput, entity, direction, baseSpeed, clientTick);

    /// <summary>Creates a direct linear velocity assignment command.</summary>
    public static SimulationCommand Velocity(Entity entity, Vector2 linearVelocity, uint clientTick = 0) =>
        new(SimulationCommandKind.SetVelocity, entity, linearVelocity, 0f, clientTick);

    /// <summary>Creates an immediate position teleport command.</summary>
    public static SimulationCommand Teleport(Entity entity, Vector2 position) =>
        new(SimulationCommandKind.Teleport, entity, position);

    /// <summary>Creates a command to despawn an entity at the start of the next tick.</summary>
    public static SimulationCommand Despawn(Entity entity) =>
        new(SimulationCommandKind.Despawn, entity, Vector2.Zero);
}
