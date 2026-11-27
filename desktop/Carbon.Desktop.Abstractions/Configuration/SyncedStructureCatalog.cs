// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Carbon.Configuration.Catalog;
using Carbon.Maps.Building;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// The server's building catalog (<c>structures.json</c>), read from the synced config (ADR-014, audit COUP-5): build
/// reach, footprints and work amounts for the client's placement preview and site markers. There is no client-side copy:
/// before the first sync <see cref="Current"/> is <see langword="null"/> and nothing can be placed.
/// </summary>
public sealed class SyncedStructureCatalog
{
    /// <summary>Config document name of <c>structures.json</c>.</summary>
    public const string DocumentName = "structures";

    private volatile StructureCatalog? _current;

    /// <summary>Gets the catalog from the last synced <c>structures.json</c>, or <see langword="null"/> before any sync.</summary>
    public StructureCatalog? Current => _current;

    /// <summary>Reads <c>structures.json</c> from a verified catalog; an absent or invalid document keeps the last one.</summary>
    /// <param name="config">The synced catalog.</param>
    /// <returns><c>true</c> when the document was present and valid.</returns>
    public bool Apply(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.TryGet(DocumentName, out ConfigCatalogEntry? entry))
        {
            return false;
        }

        try
        {
            _current = StructureCatalog.Parse(Encoding.UTF8.GetString(entry.Utf8.Span));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }
}
