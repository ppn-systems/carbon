// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Carbon.Simulation.ECS;

/// <summary>
/// Non-generic abstraction allowing <see cref="EntityManager"/> to clean up and resize reference-type attachment stores.
/// </summary>
internal interface IAttachmentStore
{
    void RemoveBySlot(int entityId);

    void GrowEntityCapacity(int newCapacity);

    void Clear();
}

/// <summary>
/// Generational-safe storage associating managed reference objects (state machines, render bridges, buffers, AI plans)
/// with ECS <see cref="Entity"/> handles while keeping hot simulation components strictly unmanaged.
/// </summary>
/// <typeparam name="T">Managed reference type stored per entity.</typeparam>
public sealed class EntityAttachmentStore<T> : IAttachmentStore where T : class
{
    private readonly EntityManager _manager;
    private T?[] _items;

    internal EntityAttachmentStore(EntityManager manager, int initialCapacity)
    {
        _manager = manager;
        _items = new T?[initialCapacity + 1];
    }

    /// <summary>
    /// Attaches or replaces the <typeparamref name="T"/> instance owned by <paramref name="entity"/>.
    /// </summary>
    /// <param name="entity">Alive target entity.</param>
    /// <param name="value">Managed instance to attach.</param>
    public void Set(Entity entity, T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!_manager.IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity {entity} is invalid or has been destroyed.");
        }

        if (entity.Id >= _items.Length)
        {
            Array.Resize(ref _items, entity.Id + 1);
        }

        _items[entity.Id] = value;
    }

    /// <summary>
    /// Attempts to retrieve the <typeparamref name="T"/> instance attached to <paramref name="entity"/>.
    /// </summary>
    /// <param name="entity">Target entity handle.</param>
    /// <param name="value">Retrieved attachment when present and entity is alive.</param>
    /// <returns><see langword="true"/> if present; otherwise, <see langword="false"/>.</returns>
    public bool TryGet(Entity entity, [NotNullWhen(true)] out T? value)
    {
        if (!_manager.IsAlive(entity) || entity.Id >= _items.Length)
        {
            value = null;
            return false;
        }

        value = _items[entity.Id];
        return value is not null;
    }

    /// <summary>
    /// Gets the existing attachment for <paramref name="entity"/> or creates and stores a new one via <paramref name="factory"/>.
    /// </summary>
    /// <param name="entity">Alive target entity.</param>
    /// <param name="factory">Factory invoked only when no attachment is currently present.</param>
    /// <returns>The existing or newly created <typeparamref name="T"/> instance.</returns>
    public T GetOrAdd(Entity entity, Func<T> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (!_manager.IsAlive(entity))
        {
            throw new InvalidOperationException($"Entity {entity} is invalid or has been destroyed.");
        }

        if (entity.Id < _items.Length && _items[entity.Id] is T existing)
        {
            return existing;
        }

        T created = factory() ?? throw new InvalidOperationException($"Attachment factory for {typeof(T).Name} returned null.");
        this.Set(entity, created);
        return created;
    }

    /// <summary>
    /// Determines whether <paramref name="entity"/> is alive and owns a <typeparamref name="T"/> attachment.
    /// </summary>
    public bool Has(Entity entity) =>
        _manager.IsAlive(entity) && entity.Id < _items.Length && _items[entity.Id] is not null;

    /// <summary>
    /// Removes the attachment owned by <paramref name="entity"/>, if present.
    /// </summary>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(Entity entity)
    {
        if (!_manager.IsAlive(entity) || entity.Id >= _items.Length || _items[entity.Id] is null)
        {
            return false;
        }

        _items[entity.Id] = null;
        return true;
    }

    void IAttachmentStore.RemoveBySlot(int entityId)
    {
        if ((uint)entityId < (uint)_items.Length)
        {
            _items[entityId] = null;
        }
    }

    void IAttachmentStore.GrowEntityCapacity(int newCapacity)
    {
        if (newCapacity + 1 > _items.Length)
        {
            Array.Resize(ref _items, newCapacity + 1);
        }
    }

    void IAttachmentStore.Clear() => Array.Clear(_items);
}
