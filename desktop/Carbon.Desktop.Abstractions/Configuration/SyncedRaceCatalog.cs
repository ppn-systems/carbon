// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text;
using Carbon.Configuration.Catalog;
using Carbon.Items.Races;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// The client's view of the server's <c>races.json</c> (ADR-014, ADR-042): which races, body types and appearance
/// options the creator offers, and which rig and palette each look renders with. The server still validates every
/// create. Owned by one session; replaced whole each time a sync completes, so readers on any thread see either the
/// old or the new catalog.
/// </summary>
public sealed class SyncedRaceCatalog
{
    private volatile RaceCatalog? _current;

    /// <summary>Raised after a verified sync installed a new catalog.</summary>
    public event Action? Changed;

    /// <summary>Gets the catalog from the last verified sync, or <see langword="null"/> before the first one.</summary>
    public RaceCatalog? Current => _current;

    /// <summary>Rebuilds the catalog from a verified config sync.</summary>
    /// <param name="config">Verified server config.</param>
    /// <returns><see langword="true"/> when <c>races.json</c> was present and valid; otherwise the previous catalog stays.</returns>
    public bool Apply(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.TryGet(RaceCatalog.DocumentName, out ConfigCatalogEntry? entry))
        {
            return false;
        }

        try
        {
            _current = RaceCatalog.LoadFromJson(Encoding.UTF8.GetString(entry.Utf8.Span));
        }
        catch (InvalidDataException)
        {
            return false;
        }

        this.Changed?.Invoke();
        return true;
    }
}
