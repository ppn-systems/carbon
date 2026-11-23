// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;

namespace Carbon.Graphics.Scenes.Management;

/// <summary>
/// Carries typed data payloads across scene transitions without relying on static global variables.
/// </summary>
public sealed class SceneTransitionData
{
    private readonly Dictionary<string, object> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new empty instance of the <see cref="SceneTransitionData"/> class.
    /// </summary>
    public SceneTransitionData()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneTransitionData"/> class with an initial key-value pair.
    /// </summary>
    /// <param name="key">The payload parameter key.</param>
    /// <param name="value">The payload value.</param>
    public SceneTransitionData(string key, object value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        ArgumentNullException.ThrowIfNull(value, nameof(value));
        _entries[key] = value;
    }

    /// <summary>
    /// Sets or updates a typed payload entry.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The lookup key.</param>
    /// <param name="value">The value to store.</param>
    public void Set<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        if (value != null)
        {
            _entries[key] = value;
        }
        else
        {
            _ = _entries.Remove(key);
        }
    }

    /// <summary>
    /// Retrieves a typed payload entry, or returns the specified fallback default if not present or type-mismatched.
    /// </summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The lookup key.</param>
    /// <param name="defaultValue">Fallback value if the key does not exist.</param>
    /// <returns>The stored payload value, or <paramref name="defaultValue"/>.</returns>
    public T Get<T>(string key, T defaultValue = default!)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        if (_entries.TryGetValue(key, out object? raw) && raw is T typed)
        {
            return typed;
        }

        return defaultValue;
    }

    /// <summary>
    /// Attempts to retrieve a strongly-typed payload entry.
    /// </summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The lookup key.</param>
    /// <param name="value">When this method returns, contains the found value; otherwise default.</param>
    /// <returns><see langword="true"/> if found and type matches; otherwise, <see langword="false"/>.</returns>
    public bool TryGet<T>(string key, out T? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        if (_entries.TryGetValue(key, out object? raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Checks whether an entry with the specified key exists in the transition payload.
    /// </summary>
    /// <param name="key">The lookup key to verify.</param>
    /// <returns><see langword="true"/> if found; otherwise, <see langword="false"/>.</returns>
    public bool Has(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        return _entries.ContainsKey(key);
    }

    /// <summary>
    /// Removes the specified entry from the transition payload.
    /// </summary>
    /// <param name="key">The entry key to remove.</param>
    /// <returns><see langword="true"/> if an entry was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key, nameof(key));
        return _entries.Remove(key);
    }
}
