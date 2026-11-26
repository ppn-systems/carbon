// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Text;
using Carbon.Configuration.Catalog;
using Carbon.Items.Catalog;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// The client's view of the server's <c>items.json</c> (ADR-014): item names, stack sizes and durability come from the
/// synced document, never from tables compiled into the client, so items the server adds show up without a client build.
/// Replaced whole each time a sync completes; readers on any thread see either the old or the new catalog.
/// </summary>
public static class SyncedItemCatalog
{
    /// <summary>Name of the synced document.</summary>
    public const string DocumentName = "items";

    private static volatile ItemCatalog? s_current;

    /// <summary>Gets the catalog from the last verified sync, or <see langword="null"/> before the first one.</summary>
    public static ItemCatalog? Current => s_current;

    /// <summary>Rebuilds the catalog from a verified config sync.</summary>
    /// <param name="config">Verified server config.</param>
    /// <returns><see langword="true"/> when <c>items.json</c> was present and valid; otherwise the previous catalog stays.</returns>
    public static bool Apply(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.TryGet(DocumentName, out ConfigCatalogEntry? entry))
        {
            return false;
        }

        try
        {
            s_current = ItemCatalog.LoadFromJson(Encoding.UTF8.GetString(entry.Utf8.Span));
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Looks up an item definition.</summary>
    /// <param name="itemId">Item id.</param>
    /// <param name="item">The definition when known.</param>
    /// <returns><see langword="true"/> when the server's catalog defines <paramref name="itemId"/>.</returns>
    public static bool TryGet(uint itemId, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out ItemCatalogEntry item)
    {
        ItemCatalog? catalog = s_current;
        if (catalog is not null)
        {
            return catalog.TryGet(itemId, out item);
        }

        item = null;
        return false;
    }

    /// <summary>Returns the display name of an item, <c>"Empty"</c> for id 0, or <c>"Item #id"</c> when unknown.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The display name.</returns>
    public static string GetDisplayName(uint itemId)
    {
        if (itemId == 0U)
        {
            return "Empty";
        }

        return TryGet(itemId, out ItemCatalogEntry? item) && item.Name.Length > 0 ? item.Name : $"Item #{itemId}";
    }

    /// <summary>Returns the maximum stack size of an item (1 when unknown).</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The maximum stack size.</returns>
    public static int GetMaxStackSize(uint itemId) =>
        TryGet(itemId, out ItemCatalogEntry? item) ? Math.Max(1, item.MaxStack) : 1;
}
