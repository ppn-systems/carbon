// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Simulation.Events;

/// <summary>
/// Non-generic event buffer contract for tick-level clearing without runtime reflection.
/// </summary>
internal interface ISimulationEventBuffer
{
    void Clear();
}

/// <summary>
/// Zero-allocation reusable buffer holding value-type simulation events emitted during a frame or tick.
/// </summary>
/// <typeparam name="T">Unmanaged event struct type.</typeparam>
public sealed class SimulationEventBuffer<T> : ISimulationEventBuffer where T : unmanaged
{
    private const int InitialCapacity = 64;

    private T[] _events = new T[InitialCapacity];
    private int _count;

    /// <summary>
    /// Gets the number of events currently buffered.
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// Appends an event instance to the buffer.
    /// </summary>
    /// <param name="simulationEvent">The event payload.</param>
    public void Publish(in T simulationEvent)
    {
        if (_count >= _events.Length)
        {
            Array.Resize(ref _events, _events.Length * 2);
        }

        _events[_count++] = simulationEvent;
    }

    /// <summary>
    /// Returns a read-only span over all buffered events.
    /// </summary>
    /// <returns>Contiguous <see cref="ReadOnlySpan{T}"/> of buffered events.</returns>
    public ReadOnlySpan<T> ReadAll() => _events.AsSpan(0, _count);

    /// <inheritdoc/>
    public void Clear() => _count = 0;
}
