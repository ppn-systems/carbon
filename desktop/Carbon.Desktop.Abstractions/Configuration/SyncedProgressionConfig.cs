// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text;
using Carbon.Configuration.Catalog;
using Carbon.Items.Progression;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// The client's view of the server's <c>progression.json</c> (ADR-014, ADR-021), used to show point budgets and to
/// preview derived stats in the character panel. The server stays authoritative for every allocation. Replaced whole
/// each time a sync completes; readers on any thread see either the old or the new config.
/// </summary>
public static class SyncedProgressionConfig
{
    private static volatile ProgressionConfig? s_current;

    /// <summary>Gets the config from the last verified sync, or <see langword="null"/> before the first one.</summary>
    public static ProgressionConfig? Current => s_current;

    /// <summary>Rebuilds the config from a verified config sync.</summary>
    /// <param name="config">Verified server config.</param>
    /// <returns><see langword="true"/> when <c>progression.json</c> was present and valid; otherwise the previous config stays.</returns>
    public static bool Apply(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.TryGet(ProgressionConfig.DocumentName, out ConfigCatalogEntry? entry))
        {
            return false;
        }

        try
        {
            s_current = ProgressionConfig.LoadFromJson(Encoding.UTF8.GetString(entry.Utf8.Span));
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
