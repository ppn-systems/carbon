// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Carbon.Configuration.Caching;

/// <summary>Process-local <see cref="IConfigCacheStorage"/>, for tests and clients without a writable disk.</summary>
public sealed class InMemoryConfigCacheStorage : IConfigCacheStorage
{
    private readonly Dictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Gets the number of cached documents.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryRead(string name, [NotNullWhen(true)] out byte[]? utf8)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_gate)
        {
            if (_entries.TryGetValue(name, out byte[]? stored))
            {
                utf8 = (byte[])stored.Clone();
                return true;
            }
        }

        utf8 = null;
        return false;
    }

    /// <inheritdoc/>
    public void Write(string name, ReadOnlySpan<byte> utf8)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_gate)
        {
            _entries[name] = utf8.ToArray();
        }
    }

    /// <inheritdoc/>
    public void Delete(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_gate)
        {
            _ = _entries.Remove(name);
        }
    }
}
