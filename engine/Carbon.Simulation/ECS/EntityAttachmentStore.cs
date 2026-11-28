// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
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
/// <remarks>
/// ADR-044: when <typeparamref name="T"/> is an <see cref="IReusableAttachment"/>, an instance released by a despawn, a
/// <see cref="Remove"/> or a replacing <see cref="Set"/> is reset and kept, and <see cref="GetOrAdd"/> hands it to the next
/// entity before calling its factory.
/// </remarks>
/// <typeparam name="T">Managed reference type stored per entity.</typeparam>
public sealed class EntityAttachmentStore<T> : IAttachmentStore where T : class
{
    private readonly EntityManager _manager;

    // Released instances, already reset. It never holds more than the peak number of instances attached at once, because
    // a new instance is created only when it is empty.
    private readonly Stack<T>? _released;
    private T?[] _items;

    internal EntityAttachmentStore(EntityManager manager, int initialCapacity)
    {
        _manager = manager;
        _items = new T?[initialCapacity + 1];
        _released = typeof(IReusableAttachment).IsAssignableFrom(typeof(T)) ? new Stack<T>() : null;
    }

    /// <summary>
    /// Gets the number of released instances kept for reuse; always 0 unless <typeparamref name="T"/> is an
    /// <see cref="IReusableAttachment"/>.
    /// </summary>
    public int ReleasedCount => _released?.Count ?? 0;

    /// <summary>
    /// Attaches or replaces the <typeparamref name="T"/> instance owned by <paramref name="entity"/>. A replaced reusable
    /// instance is released for reuse.
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

        T? previous = _items[entity.Id];
        _items[entity.Id] = value;
        if (previous is not null && !ReferenceEquals(previous, value))
        {
            this.Release(previous);
        }
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
    /// Gets the existing attachment for <paramref name="entity"/>, or attaches a released instance (reset) when one is
    /// kept, or else creates and stores a new one via <paramref name="factory"/>.
    /// </summary>
    /// <param name="entity">Alive target entity.</param>
    /// <param name="factory">Factory invoked only when no attachment is present and none is kept for reuse.</param>
    /// <returns>The existing, reused or newly created <typeparamref name="T"/> instance.</returns>
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

        T created = _released is not null && _released.TryPop(out T? reused)
            ? reused
            : factory() ?? throw new InvalidOperationException($"Attachment factory for {typeof(T).Name} returned null.");
        this.Set(entity, created);
        return created;
    }

    /// <summary>
    /// Determines whether <paramref name="entity"/> is alive and owns a <typeparamref name="T"/> attachment.
    /// </summary>
    public bool Has(Entity entity) =>
        _manager.IsAlive(entity) && entity.Id < _items.Length && _items[entity.Id] is not null;

    /// <summary>
    /// Removes the attachment owned by <paramref name="entity"/>, if present. A reusable instance is released for reuse.
    /// </summary>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(Entity entity)
    {
        if (!_manager.IsAlive(entity) || entity.Id >= _items.Length || _items[entity.Id] is not T removed)
        {
            return false;
        }

        _items[entity.Id] = null;
        this.Release(removed);
        return true;
    }

    void IAttachmentStore.RemoveBySlot(int entityId)
    {
        if ((uint)entityId < (uint)_items.Length && _items[entityId] is T removed)
        {
            _items[entityId] = null;
            this.Release(removed);
        }
    }

    void IAttachmentStore.GrowEntityCapacity(int newCapacity)
    {
        if (newCapacity + 1 > _items.Length)
        {
            Array.Resize(ref _items, newCapacity + 1);
        }
    }

    void IAttachmentStore.Clear()
    {
        Array.Clear(_items);
        _released?.Clear();
    }

    private void Release(T instance)
    {
        if (_released is not null)
        {
            ((IReusableAttachment)instance).ResetForReuse();
            _released.Push(instance);
        }
    }
}
