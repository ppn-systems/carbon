// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Simulation.ECS.Components;

namespace Carbon.Simulation.ECS;

[Flags]
internal enum ComponentFlags : byte
{
    None = 0,
    Transform = 1 << 0,
    Velocity = 1 << 1,
    Collider = 1 << 2,
    Health = 1 << 3
}

/// <summary>
/// High-throughput, zero-allocation entity and component manager.
/// Uses generational indexing and flat component arrays for optimal CPU cache locality.
/// </summary>
public sealed class EntityManager
{
    private const int InitialCapacity = 4096;

    private int _capacity;
    private int _highestId;
    private int _aliveCount;

    private int[] _generations;
    private ComponentFlags[] _flags;
    private Transform2D[] _transforms;
    private Velocity2D[] _velocities;
    private Collider2D[] _colliders;
    private HealthComponent[] _healths;

    private readonly Queue<int> _freeSlotQueue = new(512);
    private readonly List<Entity> _activeEntities = new(InitialCapacity);

    /// <summary>
    /// Gets the number of currently alive entities.
    /// </summary>
    public int EntityCount => _aliveCount;

    /// <summary>
    /// Gets the list of currently active entity handles.
    /// </summary>
    public IReadOnlyList<Entity> ActiveEntities => _activeEntities;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityManager"/> class.
    /// </summary>
    /// <param name="initialCapacity">Initial entity capacity.</param>
    public EntityManager(int initialCapacity = InitialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity, nameof(initialCapacity));

        _capacity = initialCapacity;
        _generations = new int[initialCapacity + 1];
        _flags = new ComponentFlags[initialCapacity + 1];
        _transforms = new Transform2D[initialCapacity + 1];
        _velocities = new Velocity2D[initialCapacity + 1];
        _colliders = new Collider2D[initialCapacity + 1];
        _healths = new HealthComponent[initialCapacity + 1];

        // Slot 0 is reserved for Entity.Null
        _generations[0] = 0;
        for (int i = 1; i <= initialCapacity; i++)
        {
            _generations[i] = 1;
        }
    }

    /// <summary>
    /// Spawns a new entity in the world.
    /// </summary>
    /// <returns>A unique <see cref="Entity"/> handle.</returns>
    public Entity CreateEntity()
    {
        int id;
        if (_freeSlotQueue.Count > 0)
        {
            id = _freeSlotQueue.Dequeue();
        }
        else
        {
            _highestId++;
            if (_highestId >= _capacity)
            {
                this.GrowCapacity(_capacity * 2);
            }

            id = _highestId;
        }

        int gen = _generations[id];
        _flags[id] = ComponentFlags.None;
        _aliveCount++;

        Entity entity = new(id, gen);
        _activeEntities.Add(entity);
        return entity;
    }

    /// <summary>
    /// Destroys an entity and recycles its ID slot.
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    /// <returns><see langword="true"/> if destroyed; otherwise, <see langword="false"/>.</returns>
    public bool DestroyEntity(Entity entity)
    {
        if (!this.IsAlive(entity))
        {
            return false;
        }

        int id = entity.Id;
        _generations[id]++;
        _flags[id] = ComponentFlags.None;
        _freeSlotQueue.Enqueue(id);
        _aliveCount--;

        _ = _activeEntities.Remove(entity);
        return true;
    }

    /// <summary>
    /// Verifies whether an entity handle is currently active and alive.
    /// </summary>
    /// <param name="entity">The entity handle to check.</param>
    /// <returns><see langword="true"/> if alive; otherwise, <see langword="false"/>.</returns>
    public bool IsAlive(Entity entity) =>
        entity.Id > 0 && entity.Id <= _highestId && _generations[entity.Id] == entity.Generation;

    // --- Transform2D Component ---

    /// <summary>
    /// Assigns or updates the <see cref="Transform2D"/> component of an entity.
    /// </summary>
    public void SetTransform(Entity entity, in Transform2D transform)
    {
        this.EnsureAlive(entity);
        _transforms[entity.Id] = transform;
        _flags[entity.Id] |= ComponentFlags.Transform;
    }

    /// <summary>
    /// Gets a reference to the <see cref="Transform2D"/> component of an entity.
    /// </summary>
    public ref Transform2D GetTransform(Entity entity)
    {
        this.EnsureAlive(entity);
        return ref _transforms[entity.Id];
    }

    /// <summary>
    /// Determines whether an entity has a <see cref="Transform2D"/> component.
    /// </summary>
    public bool HasTransform(Entity entity) =>
        this.IsAlive(entity) && (_flags[entity.Id] & ComponentFlags.Transform) != 0;

    // --- Velocity2D Component ---

    /// <summary>
    /// Assigns or updates the <see cref="Velocity2D"/> component of an entity.
    /// </summary>
    public void SetVelocity(Entity entity, in Velocity2D velocity)
    {
        this.EnsureAlive(entity);
        _velocities[entity.Id] = velocity;
        _flags[entity.Id] |= ComponentFlags.Velocity;
    }

    /// <summary>
    /// Gets a reference to the <see cref="Velocity2D"/> component of an entity.
    /// </summary>
    public ref Velocity2D GetVelocity(Entity entity)
    {
        this.EnsureAlive(entity);
        return ref _velocities[entity.Id];
    }

    /// <summary>
    /// Determines whether an entity has a <see cref="Velocity2D"/> component.
    /// </summary>
    public bool HasVelocity(Entity entity) =>
        this.IsAlive(entity) && (_flags[entity.Id] & ComponentFlags.Velocity) != 0;

    // --- Collider2D Component ---

    /// <summary>
    /// Assigns or updates the <see cref="Collider2D"/> component of an entity.
    /// </summary>
    public void SetCollider(Entity entity, in Collider2D collider)
    {
        this.EnsureAlive(entity);
        _colliders[entity.Id] = collider;
        _flags[entity.Id] |= ComponentFlags.Collider;
    }

    /// <summary>
    /// Gets a reference to the <see cref="Collider2D"/> component of an entity.
    /// </summary>
    public ref Collider2D GetCollider(Entity entity)
    {
        this.EnsureAlive(entity);
        return ref _colliders[entity.Id];
    }

    /// <summary>
    /// Determines whether an entity has a <see cref="Collider2D"/> component.
    /// </summary>
    public bool HasCollider(Entity entity) =>
        this.IsAlive(entity) && (_flags[entity.Id] & ComponentFlags.Collider) != 0;

    // --- HealthComponent Component ---

    /// <summary>
    /// Assigns or updates the <see cref="HealthComponent"/> of an entity.
    /// </summary>
    public void SetHealth(Entity entity, in HealthComponent health)
    {
        this.EnsureAlive(entity);
        _healths[entity.Id] = health;
        _flags[entity.Id] |= ComponentFlags.Health;
    }

    /// <summary>
    /// Gets a reference to the <see cref="HealthComponent"/> of an entity.
    /// </summary>
    public ref HealthComponent GetHealth(Entity entity)
    {
        this.EnsureAlive(entity);
        return ref _healths[entity.Id];
    }

    /// <summary>
    /// Determines whether an entity has a <see cref="HealthComponent"/>.
    /// </summary>
    public bool HasHealth(Entity entity) =>
        this.IsAlive(entity) && (_flags[entity.Id] & ComponentFlags.Health) != 0;

    /// <summary>
    /// Clears all entities and components from the manager.
    /// </summary>
    public void Clear()
    {
        _activeEntities.Clear();
        _freeSlotQueue.Clear();
        _aliveCount = 0;
        _highestId = 0;
        Array.Clear(_flags);
        for (int i = 1; i <= _capacity; i++)
        {
            _generations[i]++;
        }
    }

    private void EnsureAlive(Entity entity)
    {
        if (!this.IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity {entity} is invalid or has been destroyed.");
        }
    }

    private void GrowCapacity(int newCapacity)
    {
        int oldCap = _capacity;
        _capacity = newCapacity;

        Array.Resize(ref _generations, newCapacity + 1);
        Array.Resize(ref _flags, newCapacity + 1);
        Array.Resize(ref _transforms, newCapacity + 1);
        Array.Resize(ref _velocities, newCapacity + 1);
        Array.Resize(ref _colliders, newCapacity + 1);
        Array.Resize(ref _healths, newCapacity + 1);

        for (int i = oldCap + 1; i <= newCapacity; i++)
        {
            _generations[i] = 1;
        }
    }
}
