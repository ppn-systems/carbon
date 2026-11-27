// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Carbon.Combat.Statuses.Config;
using Carbon.Configuration.Catalog;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// Parses the status effect definitions (<c>status_effects.json</c>, ADR-029) from the server-synced config (ADR-014).
/// The session owns the result (<c>IWorldReadModel.StatusEffectDefinitions</c>); the HUD reads icons, categories and rules
/// from it, and the server only sends effect ids.
/// </summary>
public static class SyncedStatusEffectCatalog
{
    /// <summary>Parses the status effect document from <paramref name="config"/>.</summary>
    /// <param name="config">Verified config catalog.</param>
    /// <param name="catalog">The parsed definitions when the document was present and valid.</param>
    /// <returns><see langword="true"/> when the document was present and valid; otherwise the caller keeps its current copy.</returns>
    public static bool TryParse(ConfigCatalog config, out StatusEffectCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(config);
        catalog = StatusEffectCatalog.Empty;
        if (!config.TryGet(StatusEffectCatalog.DocumentName, out ConfigCatalogEntry? entry))
        {
            return false;
        }

        try
        {
            catalog = StatusEffectCatalog.LoadFromJson(Encoding.UTF8.GetString(entry.Utf8.Span));
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            return false;
        }
    }
}
