// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;
using Carbon.Data.Abstractions;
using Carbon.Data.Registry;
using Carbon.Data.Serialization;

namespace Carbon.Configuration.Catalog;

/// <summary>
/// Immutable set of verified config documents, keyed by document name (<c>items</c>, <c>monsters</c>, ...).
/// Domain loaders read their document from here instead of from disk or hardcoded tables, on both the
/// server (from <c>resources/config</c>) and the client (from the server, via <see cref="Sync.ConfigSyncClient"/>).
/// </summary>
public sealed class ConfigCatalog
{
    private readonly FrozenDictionary<string, ConfigCatalogEntry> _byName;

    /// <summary>Initializes a new instance of the <see cref="ConfigCatalog"/> class.</summary>
    /// <param name="manifestVersion">Manifest version the documents belong to.</param>
    /// <param name="entries">Documents.</param>
    /// <exception cref="InvalidOperationException">Thrown on a duplicate document name.</exception>
    public ConfigCatalog(uint manifestVersion, IEnumerable<ConfigCatalogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Dictionary<string, ConfigCatalogEntry> map = new(StringComparer.Ordinal);
        foreach (ConfigCatalogEntry entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!map.TryAdd(entry.Name, entry))
            {
                throw new InvalidOperationException($"Duplicate config document '{entry.Name}'.");
            }
        }

        this.ManifestVersion = manifestVersion;
        _byName = map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Gets the manifest version the documents belong to.</summary>
    public uint ManifestVersion { get; }

    /// <summary>Gets the number of documents.</summary>
    public int Count => _byName.Count;

    /// <summary>Gets the document names.</summary>
    public IReadOnlyCollection<string> Names => _byName.Keys;

    /// <summary>Determines whether the catalog holds <paramref name="name"/>.</summary>
    /// <param name="name">Document name.</param>
    /// <returns><see langword="true"/> when present.</returns>
    public bool Contains(string name) => _byName.ContainsKey(name);

    /// <summary>Looks up a document.</summary>
    /// <param name="name">Document name.</param>
    /// <param name="entry">The document when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryGet(string name, [MaybeNullWhen(false)] out ConfigCatalogEntry entry) => _byName.TryGetValue(name, out entry);

    /// <summary>Gets a required document.</summary>
    /// <param name="name">Document name.</param>
    /// <returns>The document.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the document is absent.</exception>
    public ConfigCatalogEntry GetRequired(string name) =>
        _byName.TryGetValue(name, out ConfigCatalogEntry? entry)
            ? entry
            : throw new KeyNotFoundException($"Config document '{name}' is not in the catalog.");

    /// <summary>Gets the UTF-8 JSON bytes of a required document, for loaders with their own document shape.</summary>
    /// <param name="name">Document name.</param>
    /// <returns>The document bytes.</returns>
    public ReadOnlySpan<byte> GetUtf8(string name) => this.GetRequired(name).Utf8.Span;

    /// <summary>
    /// Loads a document whose root is a JSON array of definitions into a <see cref="DefinitionRegistry{TKey, TDef}"/>
    /// through <see cref="JsonDefinitionLoader"/>.
    /// </summary>
    /// <typeparam name="TKey">Definition key type.</typeparam>
    /// <typeparam name="TDef">Definition type.</typeparam>
    /// <param name="name">Document name.</param>
    /// <param name="jsonTypeInfo">Source-generated metadata for <typeparamref name="TDef"/>[].</param>
    /// <param name="comparer">Optional key comparer.</param>
    /// <returns>The populated registry.</returns>
    public DefinitionRegistry<TKey, TDef> LoadRegistry<TKey, TDef>(
        string name,
        JsonTypeInfo<TDef[]> jsonTypeInfo,
        IEqualityComparer<TKey>? comparer = null)
        where TKey : notnull
        where TDef : IDataDefinition<TKey> =>
        JsonDefinitionLoader.LoadFromUtf8(this.GetUtf8(name), jsonTypeInfo, comparer);
}
