// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Carbon.Simulation.ECS.Components;

namespace Carbon.Simulation.ECS;

/// <summary>
/// High-throughput, zero-allocation entity and component manager backed by generic sparse-set
/// <see cref="ComponentPool{T}"/> containers. Supports arbitrary unmanaged struct components without runtime reflection.
/// </summary>
public sealed class EntityManager
{
    private const int InitialCapacity = 4096;

    private static int s_nextComponentTypeId;
    private static int s_nextAttachmentTypeId;

    private int _capacity;
    private int _highestId;
    private int _aliveCount;

    private int[] _generations;
    private int[] _denseIndices;
    private IComponentPool?[] _pools = new IComponentPool?[16];
    private IAttachmentStore?[] _attachmentStores = new IAttachmentStore?[8];

    private readonly ComponentPool<Transform2D> _transforms;
    private readonly ComponentPool<PreviousTransform2D> _previousTransforms;
    private readonly ComponentPool<Velocity2D> _velocities;
    private readonly ComponentPool<Collider2D> _colliders;
    private readonly ComponentPool<HealthComponent> _healths;

    private readonly Queue<int> _freeSlotQueue = new(512);
    private readonly List<Entity> _activeEntities = new(InitialCapacity);

    private static class ComponentTypeId<T> where T : unmanaged
    {
        public static readonly int Id = Interlocked.Increment(ref s_nextComponentTypeId) - 1;
    }

    private static class AttachmentTypeId<T> where T : class
    {
        public static readonly int Id = Interlocked.Increment(ref s_nextAttachmentTypeId) - 1;
    }

    /// <summary>
    /// Gets the number of currently alive entities.
    /// </summary>
    public int EntityCount => _aliveCount;

    /// <summary>
    /// Gets the list of currently active entity handles.
    /// </summary>
    public IReadOnlyList<Entity> ActiveEntities => _activeEntities;

    /// <summary>
    /// Gets a zero-allocation read-only span over the currently active entity handles.
    /// Valid until an entity is created or destroyed.
    /// </summary>
    public ReadOnlySpan<Entity> ActiveEntitiesSpan => CollectionsMarshal.AsSpan(_activeEntities);

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityManager"/> class.
    /// </summary>
    /// <param name="initialCapacity">Initial entity capacity.</param>
    public EntityManager(int initialCapacity = InitialCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialCapacity, nameof(initialCapacity));

        _capacity = initialCapacity;
        _generations = new int[initialCapacity + 1];
        _denseIndices = new int[initialCapacity + 1];
        Array.Fill(_denseIndices, -1);

        // Slot 0 is reserved for Entity.Null
        _generations[0] = 0;
        for (int i = 1; i <= initialCapacity; i++)
        {
            _generations[i] = 1;
        }

        _transforms = this.GetPool<Transform2D>();
        _previousTransforms = this.GetPool<PreviousTransform2D>();
        _velocities = this.GetPool<Velocity2D>();
        _colliders = this.GetPool<Collider2D>();
        _healths = this.GetPool<HealthComponent>();
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
        _aliveCount++;

        Entity entity = new(id, gen);
        _denseIndices[id] = _activeEntities.Count;
        _activeEntities.Add(entity);
        return entity;
    }

    /// <summary>
    /// Destroys an entity and recycles its ID slot in O(1) time using swap-back removal. Internal (ECS-8): an entity of a
    /// world leaves it only through <see cref="World.WorldSimulation.DespawnEntity"/>, which also clears its spatial-grid
    /// entry and network id and publishes the despawn.
    /// </summary>
    /// <param name="entity">The entity to destroy.</param>
    /// <returns><see langword="true"/> if destroyed; otherwise, <see langword="false"/>.</returns>
    internal bool DestroyEntity(Entity entity)
    {
        if (!this.IsAlive(entity))
        {
            return false;
        }

        int id = entity.Id;
        for (int i = 0; i < _pools.Length; i++)
        {
            _pools[i]?.RemoveBySlot(id);
        }

        for (int i = 0; i < _attachmentStores.Length; i++)
        {
            _attachmentStores[i]?.RemoveBySlot(id);
        }

        _generations[id]++;
        _freeSlotQueue.Enqueue(id);
        _aliveCount--;

        int denseIndex = _denseIndices[id];
        int lastIndex = _activeEntities.Count - 1;
        if (denseIndex != lastIndex)
        {
            Entity lastEntity = _activeEntities[lastIndex];
            _activeEntities[denseIndex] = lastEntity;
            _denseIndices[lastEntity.Id] = denseIndex;
        }

        _denseIndices[id] = -1;
        _activeEntities.RemoveAt(lastIndex);
        return true;
    }

    /// <summary>
    /// Verifies whether an entity handle is currently active and alive.
    /// </summary>
    /// <param name="entity">The entity handle to check.</param>
    /// <returns><see langword="true"/> if alive; otherwise, <see langword="false"/>.</returns>
    public bool IsAlive(Entity entity) =>
        entity.Id > 0 && entity.Id <= _highestId && _generations[entity.Id] == entity.Generation && _denseIndices[entity.Id] >= 0;

    /// <summary>
    /// Determines whether an entity slot ID is currently allocated and active.
    /// </summary>
    /// <param name="id">Entity slot identifier.</param>
    /// <returns><see langword="true"/> if active; otherwise, <see langword="false"/>.</returns>
    public bool IsSlotAlive(int id) =>
        id > 0 && id <= _highestId && _denseIndices[id] >= 0;

    /// <summary>
    /// Retrieves the active <see cref="Entity"/> handle corresponding to a given entity slot ID.
    /// </summary>
    /// <param name="id">Entity slot identifier.</param>
    /// <returns>Active entity handle, or <see cref="Entity.Null"/> if slot is inactive.</returns>
    public Entity GetEntity(int id) =>
        this.IsSlotAlive(id) ? new Entity(id, _generations[id]) : Entity.Null;

    /// <summary>
    /// Gets or creates the sparse-set <see cref="ComponentPool{T}"/> for an arbitrary unmanaged component type.
    /// </summary>
    /// <typeparam name="T">Unmanaged component struct type.</typeparam>
    /// <returns>The backing component pool.</returns>
    public ComponentPool<T> GetPool<T>() where T : unmanaged
    {
        int typeId = ComponentTypeId<T>.Id;
        if (typeId >= _pools.Length)
        {
            int nextSize = _pools.Length;
            while (nextSize <= typeId)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _pools, nextSize);
        }

        if (_pools[typeId] is not ComponentPool<T> pool)
        {
            pool = new ComponentPool<T>(_capacity);
            _pools[typeId] = pool;
        }

        return pool;
    }

    /// <summary>
    /// Gets or creates the managed <see cref="EntityAttachmentStore{T}"/> associated with reference type <typeparamref name="T"/>.
    /// Attachments are automatically cleared when their owning entity is destroyed.
    /// </summary>
    /// <typeparam name="T">Managed reference type stored per entity.</typeparam>
    /// <returns>The backing attachment store.</returns>
    public EntityAttachmentStore<T> GetAttachmentStore<T>() where T : class
    {
        int typeId = AttachmentTypeId<T>.Id;
        if (typeId >= _attachmentStores.Length)
        {
            int nextSize = _attachmentStores.Length;
            while (nextSize <= typeId)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _attachmentStores, nextSize);
        }

        if (_attachmentStores[typeId] is not EntityAttachmentStore<T> store)
        {
            store = new EntityAttachmentStore<T>(this, _capacity);
            _attachmentStores[typeId] = store;
        }

        return store;
    }

    /// <summary>
    /// Assigns or updates an unmanaged component of type <typeparamref name="T"/> on an entity.
    /// </summary>
    /// <typeparam name="T">Unmanaged component struct type.</typeparam>
    /// <param name="entity">Target entity handle.</param>
    /// <param name="component">Component value.</param>
    public void SetComponent<T>(Entity entity, in T component) where T : unmanaged
    {
        this.EnsureAlive(entity);
        this.GetPool<T>().Set(entity.Id, in component);
    }

    /// <summary>
    /// Gets a mutable reference to an entity's component of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Unmanaged component struct type.</typeparam>
    /// <param name="entity">Target entity handle.</param>
    /// <returns>Mutable reference to the component.</returns>
    public ref T GetComponent<T>(Entity entity) where T : unmanaged
    {
        this.EnsureAlive(entity);
        return ref this.GetPool<T>().Get(entity.Id);
    }

    /// <summary>
    /// Attempts to read an entity's component of type <typeparamref name="T"/> without throwing if absent.
    /// </summary>
    /// <typeparam name="T">Unmanaged component struct type.</typeparam>
    /// <param name="entity">Target entity handle.</param>
    /// <param name="component">Retrieved component value when present.</param>
    /// <returns><see langword="true"/> if the entity is alive and owns the component; otherwise, <see langword="false"/>.</returns>
    public bool TryGetComponent<T>(Entity entity, out T component) where T : unmanaged
    {
        if (!this.IsAlive(entity))
        {
            component = default;
            return false;
        }

        return this.GetPool<T>().TryGet(entity.Id, out component);
    }

    /// <summary>
    /// Determines whether an alive entity owns a component of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Unmanaged component struct type.</typeparam>
    /// <param name="entity">Target entity handle.</param>
    /// <returns><see langword="true"/> if present; otherwise, <see langword="false"/>.</returns>
    public bool HasComponent<T>(Entity entity) where T : unmanaged =>
        this.IsAlive(entity) && this.GetPool<T>().Has(entity.Id);

    /// <summary>
    /// Removes a component of type <typeparamref name="T"/> from an entity.
    /// </summary>
    /// <typeparam name="T">Unmanaged component struct type.</typeparam>
    /// <param name="entity">Target entity handle.</param>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool RemoveComponent<T>(Entity entity) where T : unmanaged =>
        this.IsAlive(entity) && this.GetPool<T>().Remove(entity.Id);

    // --- Transform2D Component ---

    /// <summary>
    /// Assigns or updates the <see cref="Transform2D"/> component of an entity.
    /// </summary>
    public void SetTransform(Entity entity, in Transform2D transform)
    {
        this.EnsureAlive(entity);
        _transforms.Set(entity.Id, in transform);
    }

    /// <summary>
    /// Gets a reference to the <see cref="Transform2D"/> component of an entity.
    /// </summary>
    public ref Transform2D GetTransform(Entity entity)
    {
        this.EnsureAlive(entity);
        return ref _transforms.Get(entity.Id);
    }

    /// <summary>
    /// Determines whether an entity has a <see cref="Transform2D"/> component.
    /// </summary>
    public bool HasTransform(Entity entity) =>
        this.IsAlive(entity) && _transforms.Has(entity.Id);

    // --- PreviousTransform2D Component ---

    /// <summary>
    /// Assigns or updates the <see cref="PreviousTransform2D"/> component of an entity.
    /// </summary>
    public void SetPreviousTransform(Entity entity, in PreviousTransform2D previousTransform)
    {
        this.EnsureAlive(entity);
        _previousTransforms.Set(entity.Id, in previousTransform);
    }

    /// <summary>
    /// Gets a reference to the <see cref="PreviousTransform2D"/> component of an entity.
    /// </summary>
    public ref PreviousTransform2D GetPreviousTransform(Entity entity)
    {
        this.EnsureAlive(entity);
        return ref _previousTransforms.Get(entity.Id);
    }

    /// <summary>
    /// Determines whether an entity has a <see cref="PreviousTransform2D"/> component.
    /// </summary>
    public bool HasPreviousTransform(Entity entity) =>
        this.IsAlive(entity) && _previousTransforms.Has(entity.Id);

    // --- Velocity2D Component ---

    /// <summary>
    /// Assigns or updates the <see cref="Velocity2D"/> component of an entity.
    /// </summary>
    public void SetVelocity(Entity entity, in Velocity2D velocity)
    {
        this.EnsureAlive(entity);
        _velocities.Set(entity.Id, in velocity);
    }

    /// <summary>
    /// Gets a reference to the <see cref="Velocity2D"/> component of an entity.
    /// </summary>
    public ref Velocity2D GetVelocity(Entity entity)
    {
        this.EnsureAlive(entity);
        return ref _velocities.Get(entity.Id);
    }

    /// <summary>
    /// Determines whether an entity has a <see cref="Velocity2D"/> component.
    /// </summary>
    public bool HasVelocity(Entity entity) =>
        this.IsAlive(entity) && _velocities.Has(entity.Id);

    // --- Collider2D Component ---

    /// <summary>
    /// Assigns or updates the <see cref="Collider2D"/> component of an entity.
    /// </summary>
    public void SetCollider(Entity entity, in Collider2D collider)
    {
        this.EnsureAlive(entity);
        _colliders.Set(entity.Id, in collider);
    }

    /// <summary>
    /// Gets a reference to the <see cref="Collider2D"/> component of an entity.
    /// </summary>
    public ref Collider2D GetCollider(Entity entity)
    {
        this.EnsureAlive(entity);
        return ref _colliders.Get(entity.Id);
    }

    /// <summary>
    /// Determines whether an entity has a <see cref="Collider2D"/> component.
    /// </summary>
    public bool HasCollider(Entity entity) =>
        this.IsAlive(entity) && _colliders.Has(entity.Id);

    // --- HealthComponent Component ---

    /// <summary>
    /// Assigns or updates the <see cref="HealthComponent"/> of an entity.
    /// </summary>
    public void SetHealth(Entity entity, in HealthComponent health)
    {
        this.EnsureAlive(entity);
        _healths.Set(entity.Id, in health);
    }

    /// <summary>
    /// Gets a reference to the <see cref="HealthComponent"/> of an entity.
    /// </summary>
    public ref HealthComponent GetHealth(Entity entity)
    {
        this.EnsureAlive(entity);
        return ref _healths.Get(entity.Id);
    }

    /// <summary>
    /// Determines whether an entity has a <see cref="HealthComponent"/>.
    /// </summary>
    public bool HasHealth(Entity entity) =>
        this.IsAlive(entity) && _healths.Has(entity.Id);

    /// <summary>
    /// Clears all entities and components from the manager.
    /// </summary>
    public void Clear()
    {
        _activeEntities.Clear();
        _freeSlotQueue.Clear();
        _aliveCount = 0;
        _highestId = 0;
        Array.Fill(_denseIndices, -1);

        for (int i = 0; i < _pools.Length; i++)
        {
            _pools[i]?.Clear();
        }

        for (int i = 0; i < _attachmentStores.Length; i++)
        {
            _attachmentStores[i]?.Clear();
        }

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
        Array.Resize(ref _denseIndices, newCapacity + 1);

        for (int i = oldCap + 1; i <= newCapacity; i++)
        {
            _generations[i] = 1;
            _denseIndices[i] = -1;
        }

        for (int i = 0; i < _pools.Length; i++)
        {
            _pools[i]?.GrowEntityCapacity(newCapacity);
        }

        for (int i = 0; i < _attachmentStores.Length; i++)
        {
            _attachmentStores[i]?.GrowEntityCapacity(newCapacity);
        }
    }
}
