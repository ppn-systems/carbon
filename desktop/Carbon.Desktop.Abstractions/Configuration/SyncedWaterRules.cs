// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text;
using Carbon.Configuration.Catalog;
using Carbon.Maps.Hydrology;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// The server's water movement and oxygen rules (<c>water.json</c>, ADR-032), read from the synced config (ADR-014).
/// Client prediction moves swimmers with them, so it matches the server's speed in every depth tier.
/// </summary>
public sealed class SyncedWaterRules
{
    private volatile WaterRules _current = WaterRules.Default;

    /// <summary>Gets the rules from the last synced <c>water.json</c>, or the shipped defaults before any sync.</summary>
    public WaterRules Current => _current;

    /// <summary>Reads <c>water.json</c> from a verified catalog.</summary>
    /// <param name="config">The synced catalog.</param>
    /// <returns><c>true</c> when the document was present and valid.</returns>
    public bool Apply(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.TryGet(WaterRules.DocumentName, out ConfigCatalogEntry? entry))
        {
            return false;
        }

        try
        {
            _current = WaterRules.LoadFromJson(Encoding.UTF8.GetString(entry.Utf8.Span));
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
