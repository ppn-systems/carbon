// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;

namespace Carbon.Maps.Tests;

/// <summary>Deterministic in-memory terrain/prop/entity environment: LushPlains at height 0 unless overridden.</summary>
internal sealed class FakeConstructionEnvironment : IConstructionEnvironment
{
    private readonly Dictionary<(int X, int Y), MapTile> _terrain = new();
    private readonly HashSet<(int X, int Y)> _props = [];
    private readonly HashSet<(int X, int Y)> _entities = [];

    public void SetBiome(int x, int y, BiomeType biome) => _terrain[(x, y)] = new MapTile(biome, 0f, 0f, 0f, false, 0, 0);

    public void SetCliff(int x, int y) => _terrain[(x, y)] = new MapTile(BiomeType.AlpinePeaks, 0f, 0f, 0f, true, 0, 0);

    public void SetProp(int x, int y) => _props.Add((x, y));

    public void SetEntity(int x, int y, bool present)
    {
        if (present)
        {
            _entities.Add((x, y));
        }
        else
        {
            _entities.Remove((x, y));
        }
    }

    public MapTile GetTerrain(int worldTileX, int worldTileY) =>
        _terrain.TryGetValue((worldTileX, worldTileY), out MapTile tile)
            ? tile
            : new MapTile(BiomeType.LushPlains, 0f, 0f, 0f, false, 0, 0);

    public bool HasSolidProp(int worldTileX, int worldTileY) => _props.Contains((worldTileX, worldTileY));

    public bool IsOccupiedByEntity(int worldTileX, int worldTileY) => _entities.Contains((worldTileX, worldTileY));
}

/// <summary>Dictionary-backed inventory implementing the material ports.</summary>
internal sealed class FakeInventory : IMaterialSource
{
    private readonly Dictionary<string, int> _items = new(StringComparer.Ordinal);

    public FakeInventory With(string itemId, int quantity)
    {
        this.Add(itemId, quantity);
        return this;
    }

    public void Add(string itemId, int quantity) => _items[itemId] = this.CountOf(itemId) + quantity;

    public int CountOf(string itemId) => _items.TryGetValue(itemId, out int count) ? count : 0;

    public int Take(string itemId, int quantity)
    {
        int taken = Math.Min(this.CountOf(itemId), quantity);
        _items[itemId] = this.CountOf(itemId) - taken;
        return taken;
    }
}

internal static class ConstructionFixtures
{
    private static readonly Lazy<StructureCatalog> s_catalog = new(() =>
        StructureCatalog.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "config", "structures.json")));

    public static StructureCatalog Catalog => s_catalog.Value;
}
