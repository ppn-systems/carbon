// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using Carbon.Economy.Config;
using Carbon.Economy.Serialization;
using Carbon.Items.Catalog;
using Carbon.Items.Definitions;

namespace Carbon.Economy.Loot;

/// <summary>
/// Validated loot tables compiled from <c>loot.json</c> against an <see cref="ItemCatalog"/> and <see cref="EconomyConfig"/>.
/// Every item code is resolved at load time, so a table can never drop a missing item at runtime.
/// </summary>
public sealed class LootCatalog
{
    /// <summary>Theme tag of items any table may drop.</summary>
    public const string GenericTag = "generic";

    private const int MaxGoldBase = 1_000_000;

    private readonly FrozenDictionary<string, LootTable> _tables;
    private readonly LootTable[] _ordered;

    private LootCatalog(LootTable[] ordered)
    {
        _ordered = ordered;
        Dictionary<string, LootTable> map = new(StringComparer.Ordinal);
        for (int i = 0; i < ordered.Length; i++)
        {
            map[ordered[i].Id] = ordered[i];
        }

        _tables = map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Gets every table in file order.</summary>
    public ReadOnlySpan<LootTable> All => _ordered;

    /// <summary>
    /// Parses, validates and compiles a <c>loot.json</c> document.
    /// </summary>
    /// <param name="json">JSON document text.</param>
    /// <param name="items">Item catalog used to resolve item codes.</param>
    /// <param name="config">Economy tunables.</param>
    /// <returns>The compiled catalog.</returns>
    /// <exception cref="InvalidDataException">Thrown when the document is malformed or inconsistent.</exception>
    public static LootCatalog LoadFromJson(string json, ItemCatalog items, EconomyConfig config)
    {
        ArgumentNullException.ThrowIfNull(json);
        LootTableDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, EconomyJsonContext.Default.LootTableDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"loot.json is malformed: {ex.Message}", ex);
        }

        return Compile(document?.Tables ?? throw new InvalidDataException("loot.json is empty."), items, config);
    }

    /// <summary>
    /// Compiles table definitions.
    /// </summary>
    /// <param name="definitions">Table definitions.</param>
    /// <param name="items">Item catalog used to resolve item codes.</param>
    /// <param name="config">Economy tunables.</param>
    /// <returns>The compiled catalog.</returns>
    /// <exception cref="InvalidDataException">Thrown when a definition is invalid.</exception>
    public static LootCatalog Compile(IReadOnlyList<LootTableDefinition> definitions, ItemCatalog items, EconomyConfig config)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(config);

        HashSet<string> ids = new(StringComparer.Ordinal);
        LootTable[] tables = new LootTable[definitions.Count];
        for (int i = 0; i < definitions.Count; i++)
        {
            LootTableDefinition definition = definitions[i];
            string id = definition.Id;
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
            {
                throw new InvalidDataException($"Loot table #{i} has an empty or duplicate id '{id}'.");
            }

            Require(definition.GoldBase is >= 0 and <= MaxGoldBase, id, "goldBase must be in [0, 1000000]");
            Require(definition.DiamondBasePpm is >= 0 and <= 1_000_000, id, "diamondBasePpm must be in [0, 1000000]");
            Require(definition.Rolls >= 0 && definition.EliteExtraRolls >= 0 && definition.BossExtraRolls >= 0, id, "rolls must be >= 0");
            Require(
                definition.MinLevel >= 1 && definition.MaxLevel >= definition.MinLevel && definition.MaxLevel <= config.MaxLevel,
                id,
                "needs 1 <= minLevel <= maxLevel <= economy maxLevel");
            Require(definition.NothingWeight >= 0, id, "nothingWeight must be >= 0");

            LootEntry[] guaranteed = CompileEntries(definition.Guaranteed, items, config, id, weighted: false);
            LootEntry[] weighted = CompileEntries(definition.Entries, items, config, id, weighted: true);
            tables[i] = new LootTable(definition, guaranteed, weighted, config);
        }

        return new LootCatalog(tables);
    }

    /// <summary>
    /// Looks up the table of a source id.
    /// </summary>
    /// <param name="sourceId">Monster or node id.</param>
    /// <param name="table">The table when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryGet(string sourceId, [MaybeNullWhen(false)] out LootTable table)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        return _tables.TryGetValue(sourceId, out table);
    }

    private static LootEntry[] CompileEntries(
        IReadOnlyList<LootEntryDefinition> definitions,
        ItemCatalog items,
        EconomyConfig config,
        string tableId,
        bool weighted)
    {
        LootEntry[] entries = new LootEntry[definitions.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            LootEntryDefinition definition = definitions[i];
            if (!items.TryGetByCode(definition.Item, out ItemCatalogEntry? item))
            {
                throw new InvalidDataException($"Loot table '{tableId}' references unknown item '{definition.Item}'.");
            }

            Require(definition.Min >= 1 && definition.Max >= definition.Min, tableId, $"'{definition.Item}' needs 1 <= min <= max");
            Require(definition.Max <= item.MaxStack, tableId, $"'{definition.Item}' max exceeds its maxStack");
            Require(!weighted || definition.Weight > 0, tableId, $"'{definition.Item}' weight must be positive");
            Require(
                definition.MinLevel >= 1 && definition.MinLevel <= config.MaxLevel && (definition.MaxLevel == 0 || definition.MaxLevel >= definition.MinLevel),
                tableId,
                $"'{definition.Item}' has an invalid level band");
            entries[i] = new LootEntry(item.Id, weighted ? definition.Weight : 0, definition.Min, definition.Max, definition.MinLevel, definition.MaxLevel);
        }

        return entries;
    }

    private static void Require(bool condition, string tableId, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException($"Loot table '{tableId}': {message}.");
        }
    }
}
