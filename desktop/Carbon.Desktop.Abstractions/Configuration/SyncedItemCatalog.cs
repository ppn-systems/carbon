// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using Carbon.Configuration.Catalog;
using Carbon.Items.Catalog;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Items.Enhancement;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// One session's view of the server's <c>items.json</c>, <c>recipes.json</c> and <c>enhancement.json</c> (ADR-014, ADR-024): item names, stats, stack sizes,
/// durability and recipes come from the synced documents, never from tables compiled into the client, so items and recipes
/// the server adds show up without a client build. The HUD, crafting window and client prediction read
/// <see cref="Rules"/>. Owned by the session (<see cref="World.IWorldReadModel.Items"/>); each verified sync replaces the
/// rules whole, so readers on any thread see either the old or the new set.
/// </summary>
public sealed class SyncedItemCatalog
{
    /// <summary>Name of the synced document.</summary>
    public const string DocumentName = "items";

    /// <summary>Name of the synced recipe document.</summary>
    public const string RecipesDocumentName = "recipes";

    /// <summary>Name of the synced forge enhancement document (ADR-024).</summary>
    public const string EnhancementDocumentName = "enhancement";

    private readonly Lazy<ItemRules> _fallback = new(ItemRules.LoadShippedDefaults);
    private volatile ItemRules? _synced;

    /// <summary>Gets the item catalog from the last verified sync, or <see langword="null"/> before the first one.</summary>
    public ItemCatalog? Current => _synced?.ItemCatalog;

    /// <summary>
    /// Gets the rules from the last verified sync. Before the first sync (title and character screens) this is the
    /// copy of the shipped tables built into <c>Carbon.Items</c>.
    /// </summary>
    public ItemRules Rules => _synced ?? _fallback.Value;

    /// <summary>Rebuilds the rules from a verified config sync.</summary>
    /// <param name="config">Verified server config.</param>
    /// <returns><see langword="true"/> when <c>items.json</c>, <c>recipes.json</c> and <c>enhancement.json</c> were present and valid; otherwise the previous rules stay.</returns>
    public bool Apply(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // Recipe ids must match the server's, so a sync without a valid recipes.json is rejected as a whole.
        if (!config.TryGet(DocumentName, out ConfigCatalogEntry? entry)
            || !config.TryGet(RecipesDocumentName, out ConfigCatalogEntry? recipesEntry)
            || !config.TryGet(EnhancementDocumentName, out ConfigCatalogEntry? enhancementEntry))
        {
            return false;
        }

        try
        {
            ItemCatalog items = ItemCatalog.LoadFromJson(Encoding.UTF8.GetString(entry.Utf8.Span));
            RecipeCatalog recipes = RecipeCatalog.LoadFromJson(Encoding.UTF8.GetString(recipesEntry.Utf8.Span), items);
            EnhancementTable enhancement = EnhancementTable.LoadFromJson(Encoding.UTF8.GetString(enhancementEntry.Utf8.Span), items);
            _synced = new ItemRules(items, recipes, enhancement);
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
    public bool TryGet(uint itemId, [MaybeNullWhen(false)] out ItemCatalogEntry item)
    {
        ItemRules? rules = _synced;
        if (rules is not null)
        {
            return rules.ItemCatalog.TryGet(itemId, out item);
        }

        item = null;
        return false;
    }

    /// <summary>Returns the display name of an item, <c>"Empty"</c> for id 0, or <c>"Item #id"</c> when unknown.</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The display name.</returns>
    public string GetDisplayName(uint itemId)
    {
        if (itemId == 0U)
        {
            return "Empty";
        }

        return this.TryGet(itemId, out ItemCatalogEntry? item) && item.Name.Length > 0 ? item.Name : $"Item #{itemId}";
    }

    /// <summary>Returns the maximum stack size of an item (1 when unknown).</summary>
    /// <param name="itemId">Item id.</param>
    /// <returns>The maximum stack size.</returns>
    public int GetMaxStackSize(uint itemId) =>
        this.TryGet(itemId, out ItemCatalogEntry? item) ? Math.Max(1, item.MaxStack) : 1;
}
