// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;

namespace Carbon.Configuration.Catalog;

/// <summary>One verified config document held by a <see cref="ConfigCatalog"/>.</summary>
public sealed class ConfigCatalogEntry
{
    private readonly byte[] _utf8;

    /// <summary>Initializes a new instance of the <see cref="ConfigCatalogEntry"/> class.</summary>
    /// <param name="name">Document name.</param>
    /// <param name="contentHash">Content hash of <paramref name="utf8"/>.</param>
    /// <param name="version">Per-document revision.</param>
    /// <param name="utf8">Canonical UTF-8 JSON bytes; the entry takes ownership of the array.</param>
    public ConfigCatalogEntry(string name, ulong contentHash, uint version, byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(utf8);
        this.Name = name;
        this.ContentHash = contentHash;
        this.Version = version;
        _utf8 = utf8;
    }

    /// <summary>Gets the document name.</summary>
    public string Name { get; }

    /// <summary>Gets the content hash.</summary>
    public ulong ContentHash { get; }

    /// <summary>Gets the per-document revision.</summary>
    public uint Version { get; }

    /// <summary>Gets the canonical UTF-8 JSON bytes.</summary>
    public ReadOnlyMemory<byte> Utf8 => _utf8;
}
