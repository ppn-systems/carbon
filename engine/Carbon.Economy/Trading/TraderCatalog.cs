// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Carbon.Economy.Config;
using Carbon.Items.Catalog;
using Carbon.Items.Definitions;

namespace Carbon.Economy.Trading;

/// <summary>
/// Trader profiles from <c>resources/config/traders.json</c> (server-only, ADR-025). Validation rejects unknown items,
/// bad quantities, and any price variance that could sell an item at or below the vendor's buy-back price, so buying
/// from a trader and selling back can never mint gold (<c>docs/economy.md</c> §1).
/// </summary>
public sealed class TraderCatalog
{
    /// <summary>File name of the document.</summary>
    public const string FileName = "traders.json";

    /// <summary>Most stock slots a profile may roll.</summary>
    public const int MaxSlots = 16;

    /// <summary>Village style names in <c>VillageStyle</c> order (bit index of <see cref="TraderStockEntry.StyleMask"/>).</summary>
    public static readonly ReadOnlyCollection<string> StyleNames = Array.AsReadOnly(["temperate", "desert", "snow", "swamp"]);

    private readonly Dictionary<string, TraderProfile> _profiles;

    private TraderCatalog(Dictionary<string, TraderProfile> profiles) => _profiles = profiles;

    /// <summary>Gets every profile.</summary>
    public IReadOnlyCollection<TraderProfile> Profiles => _profiles.Values;

    /// <summary>Looks up a profile.</summary>
    public bool TryGet(string id, out TraderProfile profile) => _profiles.TryGetValue(id, out profile!);

    /// <summary>Parses and validates the document.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="items">Item catalog (codes and base prices).</param>
    /// <param name="config">Economy tunables (vendor sell ratio).</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="InvalidDataException">The document is malformed or inconsistent.</exception>
    public static TraderCatalog LoadFromJson(string json, ItemCatalog items, EconomyConfig config)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(config);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            int maxVariance = root.TryGetProperty("maxPriceVariancePercent", out JsonElement mv) ? mv.GetInt32() : 50;

            // The cheapest roll (list × (100 − variance) %) must stay above the vendor sell ratio: no buy-sell loop mints gold.
            int ceiling = config.Vendor.BuyMarkupPercent - config.Vendor.SellRatioPercent - 1;
            if (maxVariance < 0 || maxVariance > ceiling)
            {
                throw Fail($"maxPriceVariancePercent must be within 0..{ceiling} (buy markup minus sell ratio).");
            }

            Dictionary<string, TraderProfile> profiles = new(StringComparer.Ordinal);
            foreach (JsonElement p in root.GetProperty("profiles").EnumerateArray())
            {
                TraderProfile profile = ParseProfile(p, items, maxVariance);
                if (!profiles.TryAdd(profile.Id, profile))
                {
                    throw Fail($"profile '{profile.Id}' is defined more than once.");
                }
            }

            return new TraderCatalog(profiles);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw Fail(ex.Message, ex);
        }
    }

    private static TraderProfile ParseProfile(JsonElement p, ItemCatalog items, int maxVariance)
    {
        string id = p.GetProperty("id").GetString() ?? string.Empty;
        if (id.Length == 0)
        {
            throw Fail("a profile has no id.");
        }

        int slots = p.GetProperty("slots").GetInt32();
        int variance = p.GetProperty("priceVariancePercent").GetInt32();
        int restockMinutes = p.GetProperty("restockMinutes").GetInt32();
        int jitterMinutes = p.TryGetProperty("restockJitterMinutes", out JsonElement j) ? j.GetInt32() : 0;
        if (slots is < 1 or > MaxSlots)
        {
            throw Fail($"profile '{id}' slots must be 1..{MaxSlots}.");
        }

        if (variance < 0 || variance > maxVariance)
        {
            throw Fail($"profile '{id}' priceVariancePercent must be 0..{maxVariance}.");
        }

        if (restockMinutes is < 1 or > 7 * 24 * 60 || jitterMinutes < 0 || jitterMinutes >= restockMinutes)
        {
            throw Fail($"profile '{id}' needs restockMinutes 1..10080 and 0 <= restockJitterMinutes < restockMinutes.");
        }

        List<TraderStockEntry> entries = [];
        HashSet<uint> seen = [];
        foreach (JsonElement e in p.GetProperty("entries").EnumerateArray())
        {
            string code = e.GetProperty("item").GetString() ?? string.Empty;
            if (!items.TryGetByCode(code, out ItemCatalogEntry? item))
            {
                throw Fail($"profile '{id}' stocks unknown item '{code}'.");
            }

            if (item.BasePrice <= 0)
            {
                throw Fail($"profile '{id}' stocks '{code}', which has no base price.");
            }

            int weight = e.TryGetProperty("weight", out JsonElement w) ? w.GetInt32() : 1;
            int min = e.GetProperty("min").GetInt32();
            int max = e.GetProperty("max").GetInt32();
            if (weight <= 0 || min < 1 || max < min || max > item.MaxStack * 4)
            {
                throw Fail($"profile '{id}' entry '{code}' needs weight > 0 and 1 <= min <= max <= 4 stacks.");
            }

            if (!seen.Add(item.Id))
            {
                throw Fail($"profile '{id}' lists '{code}' twice.");
            }

            byte mask = 0;
            if (e.TryGetProperty("styles", out JsonElement styles))
            {
                foreach (JsonElement s in styles.EnumerateArray())
                {
                    int index = StyleNames.IndexOf(s.GetString() ?? string.Empty);
                    if (index < 0)
                    {
                        throw Fail($"profile '{id}' entry '{code}' has unknown style '{s.GetString()}'.");
                    }

                    mask |= (byte)(1 << index);
                }
            }

            entries.Add(new TraderStockEntry(item.Id, weight, min, max, mask));
        }

        if (entries.Count < slots)
        {
            throw Fail($"profile '{id}' needs at least {slots} entries to fill its slots.");
        }

        return new TraderProfile
        {
            Id = id,
            Slots = slots,
            PriceVariancePercent = variance,
            RestockSeconds = restockMinutes * 60,
            RestockJitterSeconds = jitterMinutes * 60,
            Entries = entries.AsReadOnly(),
        };
    }

    private static InvalidDataException Fail(string message, Exception? inner = null) =>
        new($"{FileName}: {message}", inner);
}
