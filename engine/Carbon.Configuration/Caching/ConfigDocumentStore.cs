// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Carbon.Configuration.Caching;

/// <summary>
/// Hash-verified access to the client's config cache. A cached copy is only returned when its length and
/// content hash match the server manifest entry; anything else is treated as a miss and evicted.
/// </summary>
public sealed class ConfigDocumentStore
{
    private readonly IConfigCacheStorage _storage;

    /// <summary>Initializes a new instance of the <see cref="ConfigDocumentStore"/> class.</summary>
    /// <param name="storage">Underlying byte storage.</param>
    public ConfigDocumentStore(IConfigCacheStorage storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
    }

    /// <summary>Loads the cached copy of <paramref name="entry"/> when it matches the manifest.</summary>
    /// <param name="entry">Server manifest entry.</param>
    /// <param name="utf8">Verified bytes on a hit.</param>
    /// <returns><see langword="true"/> on a verified cache hit.</returns>
    public bool TryLoad(in ConfigManifestEntry entry, [NotNullWhen(true)] out byte[]? utf8)
    {
        if (_storage.TryRead(entry.Name, out byte[]? cached))
        {
            if (cached.Length == entry.ByteLength && ConfigContentHash.Compute(cached) == entry.ContentHash)
            {
                utf8 = cached;
                return true;
            }

            _storage.Delete(entry.Name);
        }

        utf8 = null;
        return false;
    }

    /// <summary>
    /// Persists a document whose bytes were already verified against the manifest. The cache only saves bandwidth on the
    /// next sync, so a storage failure (disk full, file locked by another client) is reported but never fails the sync.
    /// </summary>
    /// <param name="name">Document name.</param>
    /// <param name="utf8">Verified document bytes.</param>
    /// <returns><see langword="true"/> when the document was persisted.</returns>
    public bool Save(string name, ReadOnlySpan<byte> utf8)
    {
        ArgumentNullException.ThrowIfNull(name);
        try
        {
            _storage.Write(name, utf8);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
