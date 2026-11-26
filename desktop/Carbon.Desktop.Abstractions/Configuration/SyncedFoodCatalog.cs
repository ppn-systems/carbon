// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Text.Json;
using Carbon.Configuration.Catalog;
using Carbon.Items.Definitions;
using Carbon.Survival.Config;
using Carbon.Survival.Food;

namespace Carbon.Desktop.Abstractions.Configuration;

/// <summary>
/// The client's view of the server's <c>food.json</c> (ADR-014, ADR-016), used for HUD tooltips (nutrition and spoil
/// time). Replaced whole each time a sync completes; readers on any thread see either the old or the new catalog.
/// </summary>
public static class SyncedFoodCatalog
{
    /// <summary>Name of the synced food document.</summary>
    public const string DocumentName = "food";

    /// <summary>Name of the synced needs document (supplies the food capacity used for validation).</summary>
    public const string NeedsDocumentName = "needs";

    private static volatile FoodCatalog? s_current;

    /// <summary>Gets the catalog from the last verified sync, or <see langword="null"/> before the first one.</summary>
    public static FoodCatalog? Current => s_current;

    /// <summary>Rebuilds the catalog from a verified config sync.</summary>
    /// <param name="config">Verified server config.</param>
    /// <returns><see langword="true"/> when <c>food.json</c> was present and valid; otherwise the previous catalog stays.</returns>
    public static bool Apply(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.TryGet(DocumentName, out ConfigCatalogEntry? food))
        {
            return false;
        }

        try
        {
            float foodMax = config.TryGet(NeedsDocumentName, out ConfigCatalogEntry? needs)
                ? NeedsConfig.LoadFromJson(Encoding.UTF8.GetString(needs.Utf8.Span)).FoodMax
                : new NeedsConfig().FoodMax;
            s_current = FoodCatalog.LoadFromJson(Encoding.UTF8.GetString(food.Utf8.Span), foodMax);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Looks up the food definition of an item id via the synced item catalog.</summary>
    /// <param name="items">The session's synced item catalog (resolves the item's code).</param>
    /// <param name="itemId">Item id.</param>
    /// <param name="food">The food definition when the item is edible.</param>
    /// <returns><see langword="true"/> when the item is a food in <c>food.json</c>.</returns>
    public static bool TryGet(SyncedItemCatalog items, uint itemId, [MaybeNullWhen(false)] out FoodDefinition food)
    {
        ArgumentNullException.ThrowIfNull(items);
        FoodCatalog? catalog = s_current;
        if (catalog is not null &&
            items.TryGet(itemId, out ItemCatalogEntry? item) &&
            catalog.TryGetIndex(item.Code, out int index))
        {
            food = catalog.Get(index);
            return true;
        }

        food = null;
        return false;
    }

    /// <summary>
    /// Formats the tooltip line for a food stack, e.g. <c>"+25 food  Spoils in 12m"</c> or <c>"+40 food"</c>.
    /// </summary>
    /// <param name="food">Food definition.</param>
    /// <param name="freshnessSeconds">The stack's remaining freshness (0 = not perishable or not stamped yet).</param>
    /// <returns>The tooltip line.</returns>
    public static string FormatTooltip(FoodDefinition food, float freshnessSeconds)
    {
        ArgumentNullException.ThrowIfNull(food);

        string nutrition = $"+{(int)MathF.Round(food.Nutrition)} food";
        if (!food.IsPerishable)
        {
            return nutrition;
        }

        float seconds = freshnessSeconds > 0f ? freshnessSeconds : food.SpoilSeconds;
        return $"{nutrition}  Spoils in {FormatDuration(seconds)}";
    }

    /// <summary>Formats a duration as <c>"1h 05m"</c>, <c>"12m"</c> or <c>"45s"</c>.</summary>
    /// <param name="seconds">Duration in seconds.</param>
    /// <returns>The formatted duration.</returns>
    public static string FormatDuration(float seconds)
    {
        int total = (int)MathF.Ceiling(MathF.Max(0f, seconds));
        return total >= 3600
            ? $"{total / 3600}h {total % 3600 / 60:00}m"
            : total >= 60 ? $"{(int)MathF.Ceiling(total / 60f)}m" : $"{total}s";
    }
}
