// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Primitives.Buffers;

/// <summary>
/// Fixed power-of-two capacity circular buffer with zero heap allocations after construction.
/// Pushing when full overwrites the oldest entry.
/// </summary>
/// <typeparam name="T">Element type stored in the ring buffer.</typeparam>
public sealed class RingBuffer<T>
{
    private readonly T[] _items;
    private readonly int _mask;
    private int _head;
    private int _count;

    /// <summary>
    /// Gets the power-of-two capacity of this buffer.
    /// </summary>
    public int Capacity => _items.Length;

    /// <summary>
    /// Gets the number of elements currently stored.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Gets a value indicating whether the buffer contains no elements.
    /// </summary>
    public bool IsEmpty => _count == 0;

    /// <summary>
    /// Gets a value indicating whether the buffer is at capacity.
    /// </summary>
    public bool IsFull => _count == _items.Length;

    /// <summary>
    /// Initializes a new instance of the <see cref="RingBuffer{T}"/> class.
    /// </summary>
    /// <param name="capacity">Requested minimum capacity (rounded up to the nearest power of two).</param>
    public RingBuffer(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        int actualCapacity = (int)BitOperations.RoundUpToPowerOf2((uint)capacity);
        _items = new T[actualCapacity];
        _mask = actualCapacity - 1;
    }

    /// <summary>
    /// Gets a mutable reference to the element at logical <paramref name="index"/> (0 = oldest, <see cref="Count"/> - 1 = newest).
    /// </summary>
    /// <param name="index">Zero-based logical index from oldest element.</param>
    /// <returns>Reference to the stored element.</returns>
    public ref T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
            return ref _items[(_head + index) & _mask];
        }
    }

    /// <summary>
    /// Appends <paramref name="item"/> to the back of the buffer, overwriting the oldest element if full.
    /// </summary>
    /// <param name="item">The element to append.</param>
    public void PushBack(in T item)
    {
        if (_count == _items.Length)
        {
            _items[_head] = item;
            _head = (_head + 1) & _mask;
        }
        else
        {
            int tail = (_head + _count) & _mask;
            _items[tail] = item;
            _count++;
        }
    }

    /// <summary>
    /// Attempts to remove and return the oldest element from the front of the buffer.
    /// </summary>
    /// <param name="item">The removed element, or <see langword="default"/> when empty.</param>
    /// <returns><see langword="true"/> if an element was removed; otherwise, <see langword="false"/>.</returns>
    public bool TryPopFront(out T item)
    {
        if (_count == 0)
        {
            item = default!;
            return false;
        }

        item = _items[_head];
        _items[_head] = default!;
        _head = (_head + 1) & _mask;
        _count--;
        return true;
    }

    /// <summary>
    /// Removes all elements from the buffer without shrinking its backing array.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_items);
        _head = 0;
        _count = 0;
    }
}
