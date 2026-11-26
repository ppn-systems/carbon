// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using Carbon.Data.Registry;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Items.Serialization;

namespace Carbon.Items.Catalog;

/// <summary>
/// Validated, immutable item catalog loaded from <c>items.json</c>, indexed by numeric id and by code.
/// </summary>
public sealed class ItemCatalog : IStackSizeProvider
{
    private readonly DefinitionRegistry<uint, ItemCatalogEntry> _byId;
    private readonly FrozenDictionary<string, ItemCatalogEntry> _byCode;

    private ItemCatalog(DefinitionRegistry<uint, ItemCatalogEntry> byId, FrozenDictionary<string, ItemCatalogEntry> byCode)
    {
        _byId = byId;
        _byCode = byCode;
    }

    /// <summary>Gets every item definition in file order.</summary>
    public ReadOnlySpan<ItemCatalogEntry> All => _byId.All;

    /// <summary>Gets the number of items.</summary>
    public int Count => _byId.Count;

    /// <summary>
    /// Parses and validates an <c>items.json</c> document.
    /// </summary>
    /// <param name="json">JSON document text.</param>
    /// <returns>The validated catalog.</returns>
    /// <exception cref="InvalidDataException">Thrown when the document is malformed or inconsistent.</exception>
    public static ItemCatalog LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ItemCatalogDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, ItemsJsonContext.Default.ItemCatalogDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"items.json is malformed: {ex.Message}", ex);
        }

        return Create(document?.Items ?? throw new InvalidDataException("items.json is empty."));
    }

    /// <summary>
    /// Builds a validated catalog from definitions.
    /// </summary>
    /// <param name="items">Item definitions.</param>
    /// <returns>The validated catalog.</returns>
    /// <exception cref="InvalidDataException">Thrown when ids/codes are duplicated or fields are out of range.</exception>
    public static ItemCatalog Create(IReadOnlyList<ItemCatalogEntry> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        Dictionary<string, ItemCatalogEntry> byCode = new(StringComparer.Ordinal);
        HashSet<uint> ids = [];
        for (int i = 0; i < items.Count; i++)
        {
            ItemCatalogEntry item = items[i];
            if (item.Id == 0 || string.IsNullOrWhiteSpace(item.Code))
            {
                throw new InvalidDataException($"Item #{i} must have a non-zero id and a code.");
            }

            if (!ids.Add(item.Id))
            {
                throw new InvalidDataException($"Duplicate item id {item.Id} ('{item.Code}').");
            }

            if (!byCode.TryAdd(item.Code, item))
            {
                throw new InvalidDataException($"Duplicate item code '{item.Code}'.");
            }

            if (item.MaxStack <= 0 || item.BasePrice < 0 || item.MaxDurability < 0f || item.Tier < 0)
            {
                throw new InvalidDataException($"Item '{item.Code}' has an out-of-range maxStack/basePrice/maxDurability/tier.");
            }

            if (item.MaxDurability > 0f && item.MaxStack != 1)
            {
                throw new InvalidDataException($"Item '{item.Code}' wears out (maxDurability > 0) and must have maxStack 1.");
            }
        }

        return new ItemCatalog(new DefinitionRegistry<uint, ItemCatalogEntry>(items), byCode.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>
    /// Looks up an item by numeric id.
    /// </summary>
    /// <param name="id">Item id.</param>
    /// <param name="item">The item when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryGet(uint id, [MaybeNullWhen(false)] out ItemCatalogEntry item) => _byId.TryGet(id, out item);

    /// <summary>
    /// Looks up an item by code.
    /// </summary>
    /// <param name="code">Item code.</param>
    /// <param name="item">The item when found.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool TryGetByCode(string code, [MaybeNullWhen(false)] out ItemCatalogEntry item)
    {
        ArgumentNullException.ThrowIfNull(code);
        return _byCode.TryGetValue(code, out item);
    }

    /// <summary>
    /// Resolves an item code to its numeric id.
    /// </summary>
    /// <param name="code">Item code.</param>
    /// <returns>The numeric id.</returns>
    /// <exception cref="InvalidDataException">Thrown when the code is unknown.</exception>
    public uint ResolveId(string code) =>
        this.TryGetByCode(code, out ItemCatalogEntry? item)
            ? item.Id
            : throw new InvalidDataException($"Unknown item code '{code}'.");

    /// <inheritdoc/>
    public int GetMaxStack(uint itemId) => _byId.TryGet(itemId, out ItemCatalogEntry? item) ? item.MaxStack : 1;
}
