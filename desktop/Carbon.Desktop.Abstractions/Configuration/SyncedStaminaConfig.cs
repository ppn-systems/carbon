// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text;
using Carbon.Configuration.Catalog;
using Carbon.Survival.Stamina;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// The client's view of the server's <c>stamina.json</c> (ADR-014, ADR-033), used only to predict sprint drain and regen
/// between server syncs. The server stays authoritative for every spend. Replaced whole each time a sync completes.
/// </summary>
public static class SyncedStaminaConfig
{
    private static volatile StaminaConfig? s_current;

    /// <summary>Gets the config from the last verified sync, or <see langword="null"/> before the first one.</summary>
    public static StaminaConfig? Current => s_current;

    /// <summary>Rebuilds the config from a verified config sync.</summary>
    /// <param name="config">Verified server config.</param>
    /// <returns><see langword="true"/> when <c>stamina.json</c> was present and valid; otherwise the previous config stays.</returns>
    public static bool Apply(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.TryGet(StaminaConfig.DocumentName, out ConfigCatalogEntry? entry))
        {
            return false;
        }

        try
        {
            s_current = StaminaConfig.LoadFromJson(Encoding.UTF8.GetString(entry.Utf8.Span));
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
