// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Carbon.Physics.Contacts;
using Carbon.Physics.Narrowphase;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Events;
using Carbon.Simulation.Networking;
using Carbon.Simulation.Spatial;

namespace Carbon.Simulation.World;

/// <summary>
/// Top-level deterministic world simulation coordinator.
/// Orchestrates the fixed-tick loop, thread-safe command queue, generic ECS storage, modular simulation systems,
/// tick events, spatial grid partitioning, render interpolation, network identity lookup, and continuous physics.
/// </summary>
public sealed class WorldSimulation
{
    private const int MaxCommandsPerTick = 16384;
    private const int MaxSpatialScratch = 512;

    private static int s_nextEventTypeId;

    private readonly FixedTickClock _clock;
    private readonly EntityManager _entities;
    private readonly SpatialHashGrid2D _spatialGrid;
    private readonly ConcurrentQueue<SimulationCommand> _commandQueue = new();
    private readonly List<ISimulationSystem> _systems = new();
    private readonly Dictionary<ulong, Entity> _networkIdToEntity = new();
    private readonly Action<Entity, EntityDirtyMask> _markDirtyCallback;

    private ISimulationEventBuffer?[] _eventBuffers = new ISimulationEventBuffer?[8];
    private TileObstacleGrid? _obstacleGrid;
    private IWorldPhysics2D? _worldPhysics;

    private static class EventTypeId<T> where T : unmanaged
    {
        public static readonly int Id = Interlocked.Increment(ref s_nextEventTypeId) - 1;
    }

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
    /// Gets or sets the optional world physics provider for terrain speed scaling, continuous collision resolution,
    /// and static world raycasting. When unset, <see cref="ObstacleGrid"/> is used if present.
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
        _markDirtyCallback = this.MarkDirty;
    }

    /// <summary>
    /// Registers a modular simulation system to run during its declared <see cref="ISimulationSystem.Phase"/>.
    /// </summary>
    /// <param name="system">The system instance to register.</param>
    public void RegisterSystem(ISimulationSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (!_systems.Contains(system))
        {
            _systems.Add(system);
        }
    }

    /// <summary>
    /// Unregisters a previously registered simulation system.
    /// </summary>
    /// <param name="system">The system instance to remove.</param>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool UnregisterSystem(ISimulationSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        return _systems.Remove(system);
    }

    /// <summary>
    /// Publishes an unmanaged event struct to the current frame/tick event buffer.
    /// </summary>
    /// <typeparam name="T">Unmanaged event struct type.</typeparam>
    /// <param name="simulationEvent">Event payload to publish.</param>
    public void PublishEvent<T>(in T simulationEvent) where T : unmanaged =>
        this.GetEventBuffer<T>().Publish(in simulationEvent);

    /// <summary>
    /// Reads all buffered events of type <typeparamref name="T"/> emitted since the last clear.
    /// </summary>
    /// <typeparam name="T">Unmanaged event struct type.</typeparam>
    /// <returns>Contiguous read-only span of events.</returns>
    public ReadOnlySpan<T> ReadEvents<T>() where T : unmanaged =>
        this.GetEventBuffer<T>().ReadAll();

    /// <summary>
    /// Clears all buffered events of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Unmanaged event struct type.</typeparam>
    public void ClearEvents<T>() where T : unmanaged =>
        this.GetEventBuffer<T>().Clear();

    /// <summary>
    /// Clears every simulation event buffer across all event types.
    /// </summary>
    public void ClearAllEvents()
    {
        for (int i = 0; i < _eventBuffers.Length; i++)
        {
            _eventBuffers[i]?.Clear();
        }
    }

    /// <summary>
    /// Enqueues a simulation command from any thread to be executed sequentially at the start of the next fixed tick.
    /// </summary>
    /// <param name="command">The command to enqueue.</param>
    public void EnqueueCommand(in SimulationCommand command) => _commandQueue.Enqueue(command);

    /// <summary>
    /// Advances the simulation clock by the elapsed wall-clock time and executes all accumulated fixed ticks.
    /// Clears previous frame event buffers once before stepping ticks so consumers can read all frame events afterwards.
    /// </summary>
    /// <param name="elapsedSeconds">Elapsed wall-clock time in seconds since previous frame.</param>
    /// <returns>The number of fixed ticks evaluated.</returns>
    public int Update(double elapsedSeconds)
    {
        this.ClearAllEvents();
        return _clock.Advance(elapsedSeconds, this.Tick);
    }

    /// <summary>
    /// Executes a single discrete, deterministic simulation tick step.
    /// </summary>
    /// <param name="args">Tick contextual timing parameters.</param>
    public void Tick(FixedTickArgs args)
    {
        IWorldPhysics2D? physics = _worldPhysics ?? _obstacleGrid;

        // 0. Snapshot previous transforms before any movement or teleportation for smooth render interpolation
        this.SnapshotPreviousTransforms();

        // 1. Drain incoming thread-safe commands sequentially on the tick thread
        this.DrainCommands(physics);

        // 2. Custom PreUpdate systems
        this.RunSystems(SimulationPhase.PreUpdate, args);

        // 3. Movement System: integrate velocity -> position with CCD sub-stepping against world physics
        MovementSystem.Update(_entities, args.DeltaTime, physics, _markDirtyCallback);

        // 4. Custom Movement systems
        this.RunSystems(SimulationPhase.Movement, args);

        // 5. Collision & Spatial System: broadphase query, solid separation, and trigger overlap events
        SimulationEventBuffer<TriggerOverlapEvent> triggerBuffer = this.GetEventBuffer<TriggerOverlapEvent>();
        EntityCollisionSystem.Update(_entities, _spatialGrid, triggerBuffer, _markDirtyCallback);

        // 6. Custom Physics & PostUpdate systems
        this.RunSystems(SimulationPhase.Physics, args);
        this.RunSystems(SimulationPhase.PostUpdate, args);
    }

    /// <summary>
    /// Casts a ray against the world's static obstacle terrain, props, and structures.
    /// </summary>
    /// <param name="ray">The 2D testing ray.</param>
    /// <param name="hit">Output hit information.</param>
    /// <returns><see langword="true"/> if an obstacle was hit; otherwise, <see langword="false"/>.</returns>
    public bool Raycast(in Ray2D ray, out RaycastHit2D hit)
    {
        IWorldPhysics2D? physics = _worldPhysics ?? _obstacleGrid;
        if (physics is not null && physics.Raycast(in ray, out hit))
        {
            return true;
        }

        hit = RaycastHit2D.None;
        return false;
    }

    /// <summary>
    /// Casts a ray against dynamic entities in <see cref="SpatialGrid"/>, returning the closest intersecting entity.
    /// </summary>
    /// <param name="ray">The 2D testing ray.</param>
    /// <param name="hitEntity">The closest intersected entity handle, or <see cref="Entity.Null"/>.</param>
    /// <param name="hit">Output hit geometry.</param>
    /// <param name="layerMask">Collider layer bitmask filter.</param>
    /// <param name="ignoreEntity">Optional entity handle to exclude (e.g. the raycaster itself).</param>
    /// <param name="includeTriggers">Whether trigger colliders can be hit.</param>
    /// <returns><see langword="true"/> if an entity was hit; otherwise, <see langword="false"/>.</returns>
    public bool RaycastEntities(
        in Ray2D ray,
        out Entity hitEntity,
        out RaycastHit2D hit,
        uint layerMask = uint.MaxValue,
        Entity ignoreEntity = default,
        bool includeTriggers = false)
    {
        float maxDist = float.IsInfinity(ray.MaxDistance) ? (_spatialGrid.CellSize * 128f) : ray.MaxDistance;
        if (maxDist <= 0f)
        {
            hitEntity = Entity.Null;
            hit = RaycastHit2D.None;
            return false;
        }

        Ray2D boundedRay = new(ray.Origin, ray.Direction, maxDist);
        Vector2 rayEnd = boundedRay.GetPoint(maxDist);
        Vector2 minWorld = Vector2.Min(boundedRay.Origin, rayEnd);
        Vector2 maxWorld = Vector2.Max(boundedRay.Origin, rayEnd);

        CellCoord minCell = _spatialGrid.WorldToCell(minWorld);
        CellCoord maxCell = _spatialGrid.WorldToCell(maxWorld);
        CellCoord expandedMin = new(minCell.X - 1, minCell.Y - 1);
        CellCoord expandedMax = new(maxCell.X + 1, maxCell.Y + 1);

        Span<int> candidates = stackalloc int[MaxSpatialScratch];
        _spatialGrid.QueryRegion(in expandedMin, in expandedMax, candidates, out int candidateCount);

        bool found = false;
        float bestDist = maxDist;
        hitEntity = Entity.Null;
        hit = RaycastHit2D.None;

        for (int i = 0; i < candidateCount; i++)
        {
            Entity candidate = _entities.GetEntity(candidates[i]);
            if (!candidate.IsValid || candidate == ignoreEntity)
            {
                continue;
            }

            if (!_entities.HasTransform(candidate) || !_entities.HasCollider(candidate))
            {
                continue;
            }

            ref readonly Collider2D col = ref _entities.GetCollider(candidate);
            if (col.Kind == ColliderKind.None || (!includeTriggers && col.IsTrigger) || (col.Layer & layerMask) == 0)
            {
                continue;
            }

            Vector2 pos = _entities.GetTransform(candidate).Position;
            bool intersected = col.Kind == ColliderKind.Circle
                ? Raycaster2D.IntersectCircle(in boundedRay, new Circle2D(pos, col.Radius), out RaycastHit2D candidateHit)
                : Raycaster2D.IntersectAABB(in boundedRay, col.Box.Offset(pos), out candidateHit);

            if (intersected && candidateHit.Distance <= bestDist)
            {
                bestDist = candidateHit.Distance;
                hit = candidateHit;
                hitEntity = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Casts a ray against both static world geometry and dynamic entities, returning whichever hit is closer.
    /// </summary>
    /// <param name="ray">The 2D testing ray.</param>
    /// <param name="result">Unified hit output.</param>
    /// <param name="layerMask">Collider layer bitmask filter for entities.</param>
    /// <param name="ignoreEntity">Optional entity handle to exclude.</param>
    /// <returns><see langword="true"/> if anything was hit; otherwise, <see langword="false"/>.</returns>
    public bool RaycastWorld(
        in Ray2D ray,
        out WorldRaycastHit result,
        uint layerMask = uint.MaxValue,
        Entity ignoreEntity = default)
    {
        bool hitStatic = this.Raycast(in ray, out RaycastHit2D staticHit);
        float maxEntityDist = hitStatic ? staticHit.Distance : ray.MaxDistance;
        Ray2D entityRay = new(ray.Origin, ray.Direction, maxEntityDist);

        if (this.RaycastEntities(in entityRay, out Entity entity, out RaycastHit2D entityHit, layerMask, ignoreEntity))
        {
            result = new WorldRaycastHit(true, entityHit.Distance, entityHit.Point, entityHit.Normal, entity);
            return true;
        }

        if (hitStatic)
        {
            result = new WorldRaycastHit(true, staticHit.Distance, staticHit.Point, staticHit.Normal, Entity.Null);
            return true;
        }

        result = WorldRaycastHit.None;
        return false;
    }

    /// <summary>
    /// Finds all entities whose colliders overlap a world-space circle. Zero heap allocation.
    /// </summary>
    /// <param name="center">Circle center in world coordinates.</param>
    /// <param name="radius">Search circle radius in world units.</param>
    /// <param name="results">Caller-supplied span receiving overlapping entity handles.</param>
    /// <param name="count">Total number of overlapping entities written.</param>
    /// <param name="layerMask">Collider layer bitmask filter.</param>
    /// <param name="includeTriggers">Whether trigger colliders are included.</param>
    public void OverlapCircle(
        Vector2 center,
        float radius,
        Span<Entity> results,
        out int count,
        uint layerMask = uint.MaxValue,
        bool includeTriggers = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        count = 0;
        if (results.IsEmpty)
        {
            return;
        }

        Span<int> candidates = stackalloc int[MaxSpatialScratch];
        _spatialGrid.QueryRadius(center, radius + _spatialGrid.CellSize, candidates, out int candidateCount);
        Circle2D queryCircle = new(center, radius);

        for (int i = 0; i < candidateCount && count < results.Length; i++)
        {
            Entity candidate = _entities.GetEntity(candidates[i]);
            if (!candidate.IsValid || !_entities.HasTransform(candidate) || !_entities.HasCollider(candidate))
            {
                continue;
            }

            ref readonly Collider2D col = ref _entities.GetCollider(candidate);
            if (col.Kind == ColliderKind.None || (!includeTriggers && col.IsTrigger) || (col.Layer & layerMask) == 0)
            {
                continue;
            }

            Vector2 pos = _entities.GetTransform(candidate).Position;
            bool overlaps = col.Kind == ColliderKind.Circle
                ? CollisionDetector.TestCircleCircle(in queryCircle, new Circle2D(pos, col.Radius), out CollisionContact c) && c.HasCollision
                : CollisionDetector.TestCircleAabb(in queryCircle, col.Box.Offset(pos), out c) && c.HasCollision;

            if (overlaps)
            {
                results[count++] = candidate;
            }
        }
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
        Transform2D transform = new(position);
        _entities.SetTransform(entity, in transform);
        _entities.SetPreviousTransform(entity, new PreviousTransform2D(transform.Position, transform.Rotation));
        _entities.SetCollider(entity, Collider2D.CreateCircle(radius));
        _spatialGrid.Insert(entity.Id, position);
        this.MarkDirty(entity, EntityDirtyMask.Spawned);
        return entity;
    }

    /// <summary>
    /// Spawns a physical circular entity and binds it to a unique authoritative <paramref name="networkId"/>.
    /// </summary>
    /// <param name="networkId">Non-zero unique network identifier.</param>
    /// <param name="position">Starting world position.</param>
    /// <param name="radius">Collision radius.</param>
    /// <param name="ownerSessionId">Optional owning session identifier.</param>
    /// <returns>A new <see cref="Entity"/> handle.</returns>
    public Entity SpawnNetworkEntity(ulong networkId, Vector2 position, float radius = 0.5f, ulong ownerSessionId = 0)
    {
        if (networkId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(networkId), "Network ID must be non-zero.");
        }

        if (_networkIdToEntity.ContainsKey(networkId))
        {
            throw new InvalidOperationException($"Network ID {networkId} is already bound to an active entity.");
        }

        Entity entity = this.SpawnEntity(position, radius);
        this.BindNetworkIdentity(entity, networkId, ownerSessionId);
        return entity;
    }

    /// <summary>
    /// Associates an existing alive entity with an authoritative <paramref name="networkId"/> for O(1) lookup.
    /// </summary>
    /// <param name="entity">Target entity handle.</param>
    /// <param name="networkId">Non-zero network identifier.</param>
    /// <param name="ownerSessionId">Optional owning session identifier.</param>
    public void BindNetworkIdentity(Entity entity, ulong networkId, ulong ownerSessionId = 0)
    {
        if (!_entities.IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity {entity} is invalid or has been destroyed.");
        }

        if (networkId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(networkId), "Network ID must be non-zero.");
        }

        if (_networkIdToEntity.TryGetValue(networkId, out Entity existingOwner) && existingOwner != entity)
        {
            throw new InvalidOperationException($"Network ID {networkId} is already bound to {existingOwner}.");
        }

        if (_entities.TryGetComponent(entity, out NetworkIdentity previousIdentity) && previousIdentity.NetworkId != networkId)
        {
            _ = _networkIdToEntity.Remove(previousIdentity.NetworkId);
        }

        _entities.SetComponent(entity, new NetworkIdentity(networkId, ownerSessionId));
        _networkIdToEntity[networkId] = entity;
    }

    /// <summary>
    /// Resolves an alive <see cref="Entity"/> handle from its authoritative <paramref name="networkId"/> in O(1) time.
    /// </summary>
    /// <param name="networkId">Authoritative network identifier.</param>
    /// <param name="entity">The resolved entity handle when found.</param>
    /// <returns><see langword="true"/> if found and alive; otherwise, <see langword="false"/>.</returns>
    public bool TryGetEntityByNetworkId(ulong networkId, out Entity entity)
    {
        if (_networkIdToEntity.TryGetValue(networkId, out entity) && _entities.IsAlive(entity))
        {
            return true;
        }

        entity = Entity.Null;
        return false;
    }

    /// <summary>
    /// Attempts to read the <see cref="NetworkIdentity"/> attached to an entity.
    /// </summary>
    /// <param name="entity">Target entity handle.</param>
    /// <param name="identity">The attached network identity if present.</param>
    /// <returns><see langword="true"/> if present; otherwise, <see langword="false"/>.</returns>
    public bool TryGetNetworkIdentity(Entity entity, out NetworkIdentity identity) =>
        _entities.TryGetComponent(entity, out identity);

    /// <summary>
    /// Computes the smoothly interpolated transform of an entity between its previous and current fixed ticks.
    /// </summary>
    /// <param name="entity">Target entity handle.</param>
    /// <param name="alpha">Optional interpolation fraction [0..1]; defaults to <see cref="FixedTickClock.InterpolationAlpha"/>.</param>
    /// <returns>The interpolated <see cref="Transform2D"/>.</returns>
    public Transform2D GetInterpolatedTransform(Entity entity, float? alpha = null)
    {
        ref readonly Transform2D current = ref _entities.GetTransform(entity);
        if (!_entities.HasPreviousTransform(entity))
        {
            return current;
        }

        float t = Math.Clamp(alpha ?? _clock.InterpolationAlpha, 0f, 1f);
        ref readonly PreviousTransform2D previous = ref _entities.GetPreviousTransform(entity);
        Vector2 pos = Vector2.Lerp(previous.Position, current.Position, t);
        float rot = previous.Rotation + ((current.Rotation - previous.Rotation) * t);
        return new Transform2D(pos, rot);
    }

    /// <summary>
    /// Computes the smoothly interpolated world position of an entity between its previous and current fixed ticks.
    /// </summary>
    /// <param name="entity">Target entity handle.</param>
    /// <param name="alpha">Optional interpolation fraction [0..1]; defaults to <see cref="FixedTickClock.InterpolationAlpha"/>.</param>
    /// <returns>The interpolated 2D world position.</returns>
    public Vector2 GetInterpolatedPosition(Entity entity, float? alpha = null) =>
        this.GetInterpolatedTransform(entity, alpha).Position;

    /// <summary>
    /// Marks one or more replication dirty flags on an alive entity.
    /// </summary>
    /// <param name="entity">Target entity handle.</param>
    /// <param name="flags">Dirty flags to set.</param>
    public void MarkDirty(Entity entity, EntityDirtyMask flags)
    {
        if (!_entities.IsAlive(entity))
        {
            return;
        }

        ComponentPool<EntityDirtyMask> pool = _entities.GetPool<EntityDirtyMask>();
        if (pool.TryGet(entity.Id, out EntityDirtyMask current))
        {
            pool.Set(entity.Id, current | flags);
        }
        else
        {
            pool.Set(entity.Id, flags);
        }
    }

    /// <summary>
    /// Reads the pending replication dirty flags for an entity.
    /// </summary>
    /// <param name="entity">Target entity handle.</param>
    /// <returns>Current dirty flags, or <see cref="EntityDirtyMask.None"/>.</returns>
    public EntityDirtyMask GetDirtyFlags(Entity entity) =>
        _entities.TryGetComponent(entity, out EntityDirtyMask flags) ? flags : EntityDirtyMask.None;

    /// <summary>
    /// Clears all pending replication dirty flags on an entity.
    /// </summary>
    /// <param name="entity">Target entity handle.</param>
    public void ClearDirtyFlags(Entity entity)
    {
        if (_entities.IsAlive(entity))
        {
            _ = _entities.RemoveComponent<EntityDirtyMask>(entity);
        }
    }

    /// <summary>
    /// Despawns and cleans up an entity from the simulation world, unbinding any network identity and publishing
    /// <see cref="EntityDespawnedEvent"/>.
    /// </summary>
    /// <param name="entity">The entity to despawn.</param>
    public void DespawnEntity(Entity entity)
    {
        if (!_entities.IsAlive(entity))
        {
            return;
        }

        ulong networkId = _entities.TryGetComponent(entity, out NetworkIdentity identity) ? identity.NetworkId : 0UL;
        if (networkId != 0)
        {
            _ = _networkIdToEntity.Remove(networkId);
        }

        _ = _spatialGrid.Remove(entity.Id);
        _ = _entities.DestroyEntity(entity);
        this.PublishEvent(new EntityDespawnedEvent(entity, networkId));
    }

    private SimulationEventBuffer<T> GetEventBuffer<T>() where T : unmanaged
    {
        int typeId = EventTypeId<T>.Id;
        if (typeId >= _eventBuffers.Length)
        {
            int nextSize = _eventBuffers.Length;
            while (nextSize <= typeId)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _eventBuffers, nextSize);
        }

        if (_eventBuffers[typeId] is not SimulationEventBuffer<T> buffer)
        {
            buffer = new SimulationEventBuffer<T>();
            _eventBuffers[typeId] = buffer;
        }

        return buffer;
    }

    private void SnapshotPreviousTransforms()
    {
        ComponentPool<Transform2D> transforms = _entities.GetPool<Transform2D>();
        ReadOnlySpan<int> slots = transforms.EntitySlots;
        Span<Transform2D> values = transforms.AsSpan();

        for (int i = 0; i < slots.Length; i++)
        {
            Entity entity = _entities.GetEntity(slots[i]);
            if (entity.IsValid)
            {
                _entities.SetPreviousTransform(entity, new PreviousTransform2D(values[i].Position, values[i].Rotation));
            }
        }
    }

    private void RunSystems(SimulationPhase phase, FixedTickArgs args)
    {
        for (int i = 0; i < _systems.Count; i++)
        {
            if (_systems[i].Phase == phase)
            {
                _systems[i].Update(this, args);
            }
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
                            this.MarkDirty(cmd.Entity, EntityDirtyMask.Velocity);
                        }
                        else if (vel.Drag <= 0f)
                        {
                            vel.Linear = Vector2.Zero;
                            this.MarkDirty(cmd.Entity, EntityDirtyMask.Velocity);
                        }
                    }

                    break;

                case SimulationCommandKind.SetVelocity:
                    if (_entities.IsAlive(cmd.Entity) && _entities.HasVelocity(cmd.Entity))
                    {
                        _entities.GetVelocity(cmd.Entity).Linear = cmd.Vector;
                        this.MarkDirty(cmd.Entity, EntityDirtyMask.Velocity);
                    }

                    break;

                case SimulationCommandKind.Teleport:
                    if (_entities.IsAlive(cmd.Entity) && _entities.HasTransform(cmd.Entity))
                    {
                        ref Transform2D transform = ref _entities.GetTransform(cmd.Entity);
                        transform.Position = cmd.Vector;
                        _entities.SetPreviousTransform(cmd.Entity, new PreviousTransform2D(cmd.Vector, transform.Rotation));
                        if (_entities.HasCollider(cmd.Entity))
                        {
                            _ = _spatialGrid.Update(cmd.Entity.Id, cmd.Vector);
                        }

                        this.MarkDirty(cmd.Entity, EntityDirtyMask.Transform);
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
