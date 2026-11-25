// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Concurrent;
using System.Numerics;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Spatial;

namespace Carbon.Simulation.World;

/// <summary>
/// Top-level deterministic world simulation coordinator.
/// Orchestrates the fixed-tick loop, thread-safe command queue, entity management, spatial grid partitioning,
/// and continuous kinematic collision resolution. Runs headlessly on Dedicated Servers and on clients for prediction.
/// </summary>
public sealed class WorldSimulation
{
    private const int MaxCommandsPerTick = 16384;

    private readonly FixedTickClock _clock;
    private readonly EntityManager _entities;
    private readonly SpatialHashGrid2D _spatialGrid;
    private readonly ConcurrentQueue<SimulationCommand> _commandQueue = new();
    private TileObstacleGrid? _obstacleGrid;
    private IWorldPhysics2D? _worldPhysics;

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
    /// Gets the number of commands currently buffered in the incoming thread-safe command queue.
    /// </summary>
    public int PendingCommandCount => _commandQueue.Count;

    /// <summary>
    /// Gets or sets the optional static obstacle tile grid for terrain collisions and raycasting.
    /// </summary>
    public TileObstacleGrid? ObstacleGrid
    {
        get => _obstacleGrid;
        set => _obstacleGrid = value;
    }

    /// <summary>
    /// Gets or sets the optional world physics provider for terrain speed scaling and continuous collision resolution.
    /// When unset, <see cref="ObstacleGrid"/> is used if present.
    /// </summary>
    public IWorldPhysics2D? WorldPhysics
    {
        get => _worldPhysics;
        set => _worldPhysics = value;
    }

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
    /// Enqueues a simulation command from any thread to be executed sequentially at the start of the next fixed tick.
    /// </summary>
    /// <param name="command">The command to enqueue.</param>
    public void EnqueueCommand(in SimulationCommand command) => _commandQueue.Enqueue(command);

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
        IWorldPhysics2D? physics = _worldPhysics ?? _obstacleGrid;

        // 1. Drain incoming thread-safe commands sequentially on the tick thread
        this.DrainCommands(physics);

        // 2. Movement System: integrate velocity -> position with CCD sub-stepping against world physics
        MovementSystem.Update(_entities, args.DeltaTime, physics);

        // 3. Collision & Spatial System: broadphase query & entity-to-entity separation
        EntityCollisionSystem.Update(_entities, _spatialGrid);
    }

    /// <summary>
    /// Casts a ray against the world's static obstacle terrain.
    /// </summary>
    /// <param name="ray">The 2D testing ray.</param>
    /// <param name="hit">Output hit information.</param>
    /// <returns><see langword="true"/> if an obstacle was hit; otherwise, <see langword="false"/>.</returns>
    public bool Raycast(in Ray2D ray, out RaycastHit2D hit)
    {
        TileObstacleGrid? grid = _obstacleGrid ?? (_worldPhysics as TileObstacleGrid);
        if (grid is not null && grid.Raycast(ray, out hit))
        {
            return true;
        }

        hit = RaycastHit2D.None;
        return false;
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

    private void DrainCommands(IWorldPhysics2D? physics)
    {
        int processed = 0;
        while (processed < MaxCommandsPerTick && _commandQueue.TryDequeue(out SimulationCommand cmd))
        {
            processed++;
            switch (cmd.Kind)
            {
                case SimulationCommandKind.SetMoveInput:
                    if (_entities.IsAlive(cmd.Entity) && _entities.HasVelocity(cmd.Entity))
                    {
                        ref Velocity2D vel = ref _entities.GetVelocity(cmd.Entity);
                        if (cmd.Vector.LengthSquared() > 0.000001f && cmd.BaseSpeed > 0f)
                        {
                            Vector2 dir = Vector2.Normalize(cmd.Vector);
                            float multiplier = (physics is not null && _entities.HasTransform(cmd.Entity))
                                ? physics.GetMoveSpeedMultiplier(_entities.GetTransform(cmd.Entity).Position)
                                : 1f;
                            vel.Linear = dir * (cmd.BaseSpeed * multiplier);
                        }
                        else if (vel.Drag <= 0f)
                        {
                            vel.Linear = Vector2.Zero;
                        }
                    }

                    break;

                case SimulationCommandKind.SetVelocity:
                    if (_entities.IsAlive(cmd.Entity) && _entities.HasVelocity(cmd.Entity))
                    {
                        _entities.GetVelocity(cmd.Entity).Linear = cmd.Vector;
                    }

                    break;

                case SimulationCommandKind.Teleport:
                    if (_entities.IsAlive(cmd.Entity) && _entities.HasTransform(cmd.Entity))
                    {
                        _entities.GetTransform(cmd.Entity).Position = cmd.Vector;
                        if (_entities.HasCollider(cmd.Entity))
                        {
                            _ = _spatialGrid.Update(cmd.Entity.Id, cmd.Vector);
                        }
                    }

                    break;

                case SimulationCommandKind.Despawn:
                    this.DespawnEntity(cmd.Entity);
                    break;

                case SimulationCommandKind.None:
                default:
                    break;
            }
        }
    }
}
