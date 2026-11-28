// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using Nalix.Abstractions;

namespace Carbon.Host.Tests.Fakes;

/// <summary>
/// An <see cref="IObjectPoolManager"/> that tracks every rented instance (ADR-043 tests): it reuses returned instances
/// last-in first-out, counts a return of an instance that is not outstanding as a double return instead of pooling it
/// again, and never resets a packet itself, so a <see cref="PacketPool"/> that skipped <see cref="IPoolable.ResetForPool"/>
/// would hand the next caller a dirty packet. Thread-safe: packets are rented on the test thread and returned by the
/// dispatchers on the thread pool; <see cref="WaitForOutstanding"/> waits for a return without polling.
/// </summary>
internal sealed class RecordingPoolManager : IObjectPoolManager
{
    private readonly object _gate = new();
    private readonly HashSet<object> _outstanding = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, Stack<object>> _free = [];
    private int _doubleReturns;
    private int _rents;

    /// <summary>Gets the number of instances rented and not yet returned.</summary>
    public int Outstanding
    {
        get
        {
            lock (_gate)
            {
                return _outstanding.Count;
            }
        }
    }

    /// <summary>Gets the number of returns of an instance that was not outstanding.</summary>
    public int DoubleReturns
    {
        get
        {
            lock (_gate)
            {
                return _doubleReturns;
            }
        }
    }

    /// <summary>Gets the number of instances handed out.</summary>
    public int Rents
    {
        get
        {
            lock (_gate)
            {
                return _rents;
            }
        }
    }

    /// <summary>Waits until exactly <paramref name="count"/> instances are out, woken by every return.</summary>
    /// <param name="count">The number of outstanding instances to wait for.</param>
    /// <param name="timeout">How long to wait.</param>
    /// <returns><see langword="true"/> if that many are out.</returns>
    public bool WaitForOutstanding(int count, TimeSpan timeout)
    {
        long deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        lock (_gate)
        {
            while (_outstanding.Count != count)
            {
                long left = deadline - Environment.TickCount64;
                if (left <= 0 || !Monitor.Wait(_gate, TimeSpan.FromMilliseconds(left)))
                {
                    return _outstanding.Count == count;
                }
            }

            return true;
        }
    }

    /// <summary>Gets whether <paramref name="instance"/> is rented and not yet returned.</summary>
    public bool IsOutstanding(object instance)
    {
        lock (_gate)
        {
            return _outstanding.Contains(instance);
        }
    }

    public T Get<T>()
        where T : IPoolable, new()
    {
        lock (_gate)
        {
            T instance = _free.TryGetValue(typeof(T), out Stack<object>? free) && free.Count > 0 ? (T)free.Pop() : new T();
            _ = _outstanding.Add(instance);
            _rents++;
            return instance;
        }
    }

    public void Return<T>(T obj)
        where T : IPoolable
    {
        ArgumentNullException.ThrowIfNull(obj);
        lock (_gate)
        {
            if (!_outstanding.Remove(obj))
            {
                _doubleReturns++;
                Monitor.PulseAll(_gate);
                return;
            }

            if (!_free.TryGetValue(typeof(T), out Stack<object>? free))
            {
                free = new Stack<object>();
                _free[typeof(T)] = free;
            }

            free.Push(obj);
            Monitor.PulseAll(_gate);
        }
    }

    public int Prealloc<T>(int count)
        where T : IPoolable, new() => 0;

    public bool SetMaxCapacity<T>(int maxCapacity)
        where T : IPoolable => true;

    public string GenerateReport() => throw new NotSupportedException();

    public void WriteReportData(System.Text.Json.Utf8JsonWriter writer) => throw new NotSupportedException();
}
