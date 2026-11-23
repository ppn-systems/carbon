// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Graphics.Assets.Abstractions;

/// <summary>
/// Defines the contract for tracking, deduplicating, and disposing loaded GPU asset instances.
/// Guarantees that multiple requests for the same resource path share a single VRAM allocation.
/// </summary>
public interface IAssetCache : IDisposable
{
    /// <summary>
    /// Gets the number of distinct assets currently residing in the cache.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Attempts to retrieve a cached asset by its resource key.
    /// </summary>
    /// <typeparam name="T">The asset type.</typeparam>
    /// <param name="key">The normalized asset path or identifier.</param>
    /// <param name="asset">When this method returns, contains the cached asset instance; otherwise null.</param>
    /// <returns><see langword="true"/> if found; otherwise, <see langword="false"/>.</returns>
    bool TryGet<T>(string key, out T? asset) where T : class;

    /// <summary>
    /// Stores an asset in the cache under the specified resource key.
    /// </summary>
    /// <typeparam name="T">The asset type.</typeparam>
    /// <param name="key">The normalized asset path or identifier.</param>
    /// <param name="asset">The asset instance to store.</param>
    void Store<T>(string key, T asset) where T : class;

    /// <summary>
    /// Unloads and disposes a specific asset by its resource key.
    /// </summary>
    /// <param name="key">The asset key to remove.</param>
    /// <returns><see langword="true"/> if an asset was found and unloaded; otherwise, <see langword="false"/>.</returns>
    bool Unload(string key);

    /// <summary>
    /// Unloads and disposes all assets currently stored in this cache.
    /// </summary>
    void Clear();
}
