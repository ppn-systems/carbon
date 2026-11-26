// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;

namespace Carbon.Configuration.Caching;

/// <summary>
/// Raw byte storage for cached config documents, keyed by document name. Storage is untrusted:
/// <see cref="ConfigDocumentStore"/> re-hashes everything it reads before use.
/// </summary>
public interface IConfigCacheStorage
{
    /// <summary>Reads the cached bytes of <paramref name="name"/>.</summary>
    /// <param name="name">Document name.</param>
    /// <param name="utf8">Cached bytes when present.</param>
    /// <returns><see langword="true"/> when a cached copy exists and could be read.</returns>
    bool TryRead(string name, [NotNullWhen(true)] out byte[]? utf8);

    /// <summary>Replaces the cached bytes of <paramref name="name"/>.</summary>
    /// <param name="name">Document name.</param>
    /// <param name="utf8">Verified document bytes.</param>
    void Write(string name, ReadOnlySpan<byte> utf8);

    /// <summary>Removes the cached copy of <paramref name="name"/>, if any.</summary>
    /// <param name="name">Document name.</param>
    void Delete(string name);
}
