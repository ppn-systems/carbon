// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Spatial;

namespace Carbon.Simulation.World;

/// <summary>
/// Top-level deterministic world simulation coordinator.
/// Orchestrates the fixed-tick loop, entity management, spatial grid partitioning, and kinematic collision resolution.
/// Runs headlessly on Dedicated Servers and on client instances for deterministic simulation and prediction.
/// </summary>
public sealed class WorldSimulation
{
    private readonly FixedTickClock _clock;
    private readonly EntityManager _entities;
    private readonly SpatialHashGrid2D _spatialGrid;

    /// <summary>
    /// Gets the fixed-tick simulation clock.
    /// </summary>
    public FixedTickClock Clock => _clock;

    /// <summary>
    /// Gets the entity manager tracking active entities and components.
    /// </summary>
    public EntityManager Entities => _entities;

    /// <summary>
    /// Gets the 2D spatial hash grid managing entity AoI and broadphase buckets.
    /// </summary>
    public SpatialHashGrid2D SpatialGrid => _spatialGrid;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldSimulation"/> class.
    /// </summary>
    /// <param name="tickRate">Fixed simulation frequency in Hertz (default 30).</param>
    /// <param name="cellSize">Spatial partitioning cell dimension in world units (default 32f).</param>
    public WorldSimulation(int tickRate = 30, float cellSize = 32f)
    {
        _clock = new FixedTickClock(tickRate);
        _entities = new EntityManager();
        _spatialGrid = new SpatialHashGrid2D(cellSize);
    }

    /// <summary>
    /// Advances the simulation clock by the elapsed wall-clock time and executes all accumulated fixed ticks.
    /// </summary>
    /// <param name="elapsedSeconds">Elapsed wall-clock time in seconds since previous frame.</param>
    /// <returns>The number of fixed ticks evaluated.</returns>
    public int Update(double elapsedSeconds) =>
        _clock.Advance(elapsedSeconds, this.Tick);

    /// <summary>
    /// Executes a single discrete, deterministic simulation tick step.
    /// </summary>
    /// <param name="args">Tick contextual timing parameters.</param>
    public void Tick(FixedTickArgs args)
    {
        // 1. Movement System: integrate velocity -> position
        MovementSystem.Update(_entities, args.DeltaTime);

        // 2. Collision & Spatial System: broadphase query & entity-to-entity separation
        EntityCollisionSystem.Update(_entities, _spatialGrid);
    }

    /// <summary>
    /// Spawns a physical circular entity in the simulation world.
    /// </summary>
    /// <param name="position">Starting world position.</param>
    /// <param name="radius">Collision radius.</param>
    /// <returns>A new <see cref="Entity"/> handle.</returns>
    public Entity SpawnEntity(Vector2 position, float radius = 0.5f)
    {
        Entity entity = _entities.CreateEntity();
        _entities.SetTransform(entity, new Transform2D(position));
        _entities.SetCollider(entity, Collider2D.CreateCircle(radius));
        _spatialGrid.Insert(entity.Id, position);
        return entity;
    }

    /// <summary>
    /// Despawns and cleans up an entity from the simulation world.
    /// </summary>
    /// <param name="entity">The entity to despawn.</param>
    public void DespawnEntity(Entity entity)
    {
        if (_entities.IsAlive(entity))
        {
            _ = _spatialGrid.Remove(entity.Id);
            _ = _entities.DestroyEntity(entity);
        }
    }
}
