// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using Carbon.Configuration.Catalog;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Items.Definitions;

namespace Carbon.Desktop.Abstractions.Localization;

/// <summary>
/// Maps a replicated entity's stable ids to its name key (ADR-023 D4). The server sends English names only as a
/// fallback; the client localizes monsters, NPCs and ground loot from <c>ArchetypeId</c>. Player names are not keys.
/// Owned by one session; keys are built once per archetype, so spawning does not allocate after the first one.
/// </summary>
public sealed class EntityNameKeys
{
    /// <summary>Name of the synced monster document.</summary>
    public const string MonstersDocumentName = "monsters";

    /// <summary>Key of a gold pile on the ground.</summary>
    public const string GoldKey = "loot.gold";

    /// <summary>Key of a diamond pile on the ground.</summary>
    public const string DiamondKey = "loot.diamonds";

    private readonly Lock _gate = new();
    private readonly SyncedItemCatalog _items;
    private readonly Dictionary<ushort, string> _npcKeys = [];
    private readonly Dictionary<uint, string> _itemKeys = [];
    private Dictionary<ushort, string> _monsterKeys = [];

    /// <summary>Initializes a new instance of the <see cref="EntityNameKeys"/> class.</summary>
    /// <param name="items">The session's synced item catalog (resolves loot item codes).</param>
    public EntityNameKeys(SyncedItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
    }

    /// <summary>Returns the localization key of an NPC from its catalog name (<c>Village Guard</c> → <c>npc.village_guard.name</c>).</summary>
    /// <param name="specName">The NPC archetype name in <c>WorldActorCatalog</c>.</param>
    /// <returns>The key.</returns>
    public static string NpcKey(string specName)
    {
        ArgumentNullException.ThrowIfNull(specName);
        Span<char> id = specName.Length <= 64 ? stackalloc char[specName.Length] : new char[specName.Length];
        for (int i = 0; i < specName.Length; i++)
        {
            char c = specName[i];
            id[i] = char.IsAsciiLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_';
        }

        return string.Concat("npc.", id, ".name");
    }

    /// <summary>Rebuilds the monster map from a verified config sync (<c>monsters.json</c>: <c>archetypeId</c> → <c>id</c>).</summary>
    /// <param name="config">Verified server config.</param>
    /// <returns><see langword="true"/> when the document was present and valid; otherwise the previous map stays.</returns>
    public bool Apply(ConfigCatalog config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!config.TryGet(MonstersDocumentName, out ConfigCatalogEntry? entry))
        {
            return false;
        }

        try
        {
            Dictionary<ushort, string> keys = [];
            using JsonDocument document = JsonDocument.Parse(entry.Utf8);
            foreach (JsonElement monster in document.RootElement.GetProperty(MonstersDocumentName).EnumerateArray())
            {
                if (monster.GetProperty("id").GetString() is { Length: > 0 } id)
                {
                    _ = keys.TryAdd(monster.GetProperty("archetypeId").GetUInt16(), string.Concat("monster.", id, ".name"));
                }
            }

            lock (_gate)
            {
                _monsterKeys = keys;
                _itemKeys.Clear();
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Resolves the name key of a replicated entity.</summary>
    /// <param name="kind">Entity kind.</param>
    /// <param name="archetypeId">Monster or NPC archetype, or the loot item id.</param>
    /// <param name="currency">For loot: whether this is a gold or diamond pile.</param>
    /// <param name="npcSpecName">For NPCs: the archetype's catalog name (see <see cref="NpcKey"/>).</param>
    /// <returns>The key, or <see langword="null"/> when the entity keeps the name the server sent (players, unknown ids).</returns>
    public string? Resolve(EntityKind kind, ushort archetypeId, LootCurrency currency, string? npcSpecName)
    {
        lock (_gate)
        {
            if (kind == EntityKind.Monster)
            {
                return _monsterKeys.GetValueOrDefault(archetypeId);
            }

            if (kind == EntityKind.Npc && npcSpecName is { Length: > 0 })
            {
                if (!_npcKeys.TryGetValue(archetypeId, out string? npc))
                {
                    npc = NpcKey(npcSpecName);
                    _npcKeys[archetypeId] = npc;
                }

                return npc;
            }

            if (kind != EntityKind.LootDrop)
            {
                return null;
            }

            return currency == LootCurrency.Gold ? GoldKey : currency == LootCurrency.Diamond ? DiamondKey : this.ItemKeyLocked(archetypeId);
        }
    }

    private string? ItemKeyLocked(uint itemId)
    {
        if (_itemKeys.TryGetValue(itemId, out string? key))
        {
            return key;
        }

        if (!_items.TryGet(itemId, out ItemCatalogEntry? item))
        {
            return null;
        }

        key = string.Concat("item.", item.Code, ".name");
        _itemKeys[itemId] = key;
        return key;
    }
}
