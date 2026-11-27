// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text;
using Carbon.Configuration.Catalog;
using Carbon.Survival.Stamina;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// The client's view of the server's <c>stamina.json</c> (ADR-014, ADR-033), used only to predict sprint drain and regen
/// between server syncs. The server stays authoritative for every spend. Owned by one session; replaced whole each time a
/// sync completes, so readers on any thread see either the old or the new config.
/// </summary>
public sealed class SyncedStaminaConfig
{
    private volatile StaminaConfig? _current;

    /// <summary>Gets the config from the last verified sync, or <see langword="null"/> before the first one.</summary>
    public StaminaConfig? Current => _current;

    /// <summary>Rebuilds the config from a verified config sync.</summary>
    /// <param name="config">Verified server config.</param>
    /// <returns><see langword="true"/> when <c>stamina.json</c> was present and valid; otherwise the previous config stays.</returns>
    public bool Apply(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.TryGet(StaminaConfig.DocumentName, out ConfigCatalogEntry? entry))
        {
            return false;
        }

        try
        {
            _current = StaminaConfig.LoadFromJson(Encoding.UTF8.GetString(entry.Utf8.Span));
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
