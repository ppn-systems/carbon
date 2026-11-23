// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Threading;
using Carbon.Graphics.Assets.Abstractions;

namespace Carbon.Graphics.Assets.Management;

/// <summary>
/// Thread-safe in-memory cache mapping normalized resource paths to active GPU asset instances.
/// Eliminates duplicate VRAM allocations and provides deterministic disposal.
/// </summary>
public sealed class AssetCache : IAssetCache
{
    private readonly Dictionary<string, object> _assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _lock = new();
    private bool _disposed;

    /// <inheritdoc/>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _assets.Count;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryGet<T>(string key, out T? asset) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        string normalized = NormalizeKey(key);

        lock (_lock)
        {
            if (_assets.TryGetValue(normalized, out object? raw) && raw is T typed)
            {
                asset = typed;
                return true;
            }
        }

        asset = null;
        return false;
    }

    /// <inheritdoc/>
    public void Store<T>(string key, T asset) where T : class
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        ArgumentNullException.ThrowIfNull(asset, nameof(asset));

        string normalized = NormalizeKey(key);

        lock (_lock)
        {
            if (_assets.TryGetValue(normalized, out object? existing) && !ReferenceEquals(existing, asset))
            {
                if (existing is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            _assets[normalized] = asset;
        }
    }

    /// <inheritdoc/>
    public bool Unload(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        string normalized = NormalizeKey(key);

        object? target = null;
        lock (_lock)
        {
            if (_assets.Remove(normalized, out object? found))
            {
                target = found;
            }
        }

        if (target != null)
        {
            if (target is IDisposable disposable)
            {
                disposable.Dispose();
            }

            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        List<object> toDispose;
        lock (_lock)
        {
            toDispose = [.. _assets.Values];
            _assets.Clear();
        }

        for (int i = 0; i < toDispose.Count; i++)
        {
            if (toDispose[i] is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        this.Clear();
    }

    private static string NormalizeKey(string key) =>
        key.Replace('\\', '/').Trim();
}
