// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Simulation.ECS;

/// <summary>
/// Non-generic pool contract allowing <see cref="EntityManager"/> to manage heterogeneous component pools
/// without runtime reflection or boxing.
/// </summary>
internal interface IComponentPool
{
    void RemoveBySlot(int slotId);

    void GrowEntityCapacity(int newEntityCapacity);

    void Clear();
}

/// <summary>
/// Cache-friendly sparse-set storage for an unmanaged ECS component type <typeparamref name="T"/>.
/// Guarantees O(1) lookup, insertion, and swap-back removal with zero heap allocations in steady-state ticks.
/// </summary>
/// <typeparam name="T">Unmanaged component struct type.</typeparam>
public sealed class ComponentPool<T> : IComponentPool where T : unmanaged
{
    private const int InitialDenseCapacity = 64;

    private int[] _sparse;
    private int[] _denseSlots;
    private T[] _denseValues;
    private int _count;

    /// <summary>
    /// Gets the number of entities currently holding this component.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Gets a read-only span of packed entity slot identifiers corresponding 1:1 with <see cref="AsSpan"/>.
    /// </summary>
    public ReadOnlySpan<int> EntitySlots => _denseSlots.AsSpan(0, _count);

    /// <summary>
    /// Initializes a new instance of the <see cref="ComponentPool{T}"/> class.
    /// </summary>
    /// <param name="entityCapacity">Initial maximum entity slot capacity.</param>
    /// <param name="initialDenseCapacity">Initial dense array capacity.</param>
    public ComponentPool(int entityCapacity, int initialDenseCapacity = InitialDenseCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(entityCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(initialDenseCapacity);

        _sparse = new int[entityCapacity + 1];
        Array.Fill(_sparse, -1);
        _denseSlots = new int[initialDenseCapacity];
        _denseValues = new T[initialDenseCapacity];
    }

    /// <summary>
    /// Returns a contiguous mutable span over all stored component instances for sequential iteration.
    /// </summary>
    /// <returns>Contiguous <see cref="Span{T}"/> of component values.</returns>
    public Span<T> AsSpan() => _denseValues.AsSpan(0, _count);

    /// <summary>
    /// Determines whether the specified entity slot currently owns an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <param name="slotId">Entity slot identifier.</param>
    /// <returns><see langword="true"/> if present; otherwise, <see langword="false"/>.</returns>
    public bool Has(int slotId) => slotId > 0 && slotId < _sparse.Length && _sparse[slotId] >= 0;

    /// <summary>
    /// Assigns or replaces the component value for the specified entity slot.
    /// </summary>
    /// <param name="slotId">Entity slot identifier.</param>
    /// <param name="component">The component value to store.</param>
    public void Set(int slotId, in T component)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(slotId);

        if (slotId >= _sparse.Length)
        {
            this.GrowEntityCapacity(Math.Max(slotId, (_sparse.Length - 1) * 2));
        }

        int denseIdx = _sparse[slotId];
        if (denseIdx >= 0)
        {
            _denseValues[denseIdx] = component;
            return;
        }

        if (_count >= _denseValues.Length)
        {
            int nextCap = _denseValues.Length * 2;
            Array.Resize(ref _denseSlots, nextCap);
            Array.Resize(ref _denseValues, nextCap);
        }

        _denseSlots[_count] = slotId;
        _denseValues[_count] = component;
        _sparse[slotId] = _count;
        _count++;
    }

    /// <summary>
    /// Gets a mutable reference to the component stored for the specified entity slot.
    /// </summary>
    /// <param name="slotId">Entity slot identifier.</param>
    /// <returns>Mutable reference to the stored component.</returns>
    public ref T Get(int slotId)
    {
        if (!this.Has(slotId))
        {
            throw new InvalidOperationException($"Entity slot {slotId} does not have component {typeof(T).Name}.");
        }

        return ref _denseValues[_sparse[slotId]];
    }

    /// <summary>
    /// Attempts to read the component value for the specified entity slot.
    /// </summary>
    /// <param name="slotId">Entity slot identifier.</param>
    /// <param name="component">The retrieved component value if present.</param>
    /// <returns><see langword="true"/> if found; otherwise, <see langword="false"/>.</returns>
    public bool TryGet(int slotId, out T component)
    {
        if (this.Has(slotId))
        {
            component = _denseValues[_sparse[slotId]];
            return true;
        }

        component = default;
        return false;
    }

    /// <summary>
    /// Removes the component from the specified entity slot in O(1) using swap-back packing.
    /// </summary>
    /// <param name="slotId">Entity slot identifier.</param>
    /// <returns><see langword="true"/> if removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(int slotId)
    {
        if (!this.Has(slotId))
        {
            return false;
        }

        int removedIdx = _sparse[slotId];
        int lastIdx = _count - 1;
        if (removedIdx != lastIdx)
        {
            int movedSlot = _denseSlots[lastIdx];
            _denseSlots[removedIdx] = movedSlot;
            _denseValues[removedIdx] = _denseValues[lastIdx];
            _sparse[movedSlot] = removedIdx;
        }

        _sparse[slotId] = -1;
        _count--;
        return true;
    }

    /// <inheritdoc/>
    void IComponentPool.RemoveBySlot(int slotId) => _ = this.Remove(slotId);

    /// <inheritdoc/>
    void IComponentPool.GrowEntityCapacity(int newEntityCapacity) => this.GrowEntityCapacity(newEntityCapacity);

    /// <inheritdoc/>
    public void Clear()
    {
        for (int i = 0; i < _count; i++)
        {
            _sparse[_denseSlots[i]] = -1;
        }

        _count = 0;
    }

    private void GrowEntityCapacity(int newEntityCapacity)
    {
        int oldLength = _sparse.Length;
        if (newEntityCapacity + 1 <= oldLength)
        {
            return;
        }

        Array.Resize(ref _sparse, newEntityCapacity + 1);
        Array.Fill(_sparse, -1, oldLength, _sparse.Length - oldLength);
    }
}
