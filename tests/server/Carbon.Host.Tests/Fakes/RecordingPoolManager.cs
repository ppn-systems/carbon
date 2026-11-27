// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Nalix.Abstractions;

namespace Carbon.Host.Tests.Fakes;

/// <summary>
/// An <see cref="IObjectPoolManager"/> that tracks every rented instance (ADR-043 tests): it reuses returned instances
/// last-in first-out, counts a return of an instance that is not outstanding as a double return instead of pooling it
/// again, and never resets a packet itself, so a <see cref="PacketPool"/> that skipped <see cref="IPoolable.ResetForPool"/>
/// would hand the next caller a dirty packet.
/// </summary>
internal sealed class RecordingPoolManager : IObjectPoolManager
{
    private readonly HashSet<object> _outstanding = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, Stack<object>> _free = [];

    /// <summary>Gets the number of instances rented and not yet returned.</summary>
    public int Outstanding => _outstanding.Count;

    /// <summary>Gets the number of returns of an instance that was not outstanding.</summary>
    public int DoubleReturns { get; private set; }

    /// <summary>Gets the number of instances handed out.</summary>
    public int Rents { get; private set; }

    /// <summary>Gets whether <paramref name="instance"/> is rented and not yet returned.</summary>
    public bool IsOutstanding(object instance) => _outstanding.Contains(instance);

    public T Get<T>()
        where T : IPoolable, new()
    {
        T instance = _free.TryGetValue(typeof(T), out Stack<object>? free) && free.Count > 0 ? (T)free.Pop() : new T();
        _ = _outstanding.Add(instance);
        this.Rents++;
        return instance;
    }

    public void Return<T>(T obj)
        where T : IPoolable
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (!_outstanding.Remove(obj))
        {
            this.DoubleReturns++;
            return;
        }

        if (!_free.TryGetValue(typeof(T), out Stack<object>? free))
        {
            free = new Stack<object>();
            _free[typeof(T)] = free;
        }

        free.Push(obj);
    }

    public int Prealloc<T>(int count)
        where T : IPoolable, new() => 0;

    public bool SetMaxCapacity<T>(int maxCapacity)
        where T : IPoolable => true;

    public string GenerateReport() => throw new NotSupportedException();

    public void WriteReportData(System.Text.Json.Utf8JsonWriter writer) => throw new NotSupportedException();
}
