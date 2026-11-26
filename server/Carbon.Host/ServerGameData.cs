// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Carbon.Application.World;
using Carbon.Configuration.Catalog;
using Carbon.Economy;
using Carbon.Maps.Building;
using Carbon.Server.Configuration;
using Carbon.Survival.Config;
using Carbon.Survival.Food;

namespace Carbon.Host;

/// <summary>
/// The server domains built from one config snapshot (ADR-014). Boot and every hot reload build them the same way,
/// so the server always runs exactly the documents it serves.
/// </summary>
/// <param name="Economy">Items, recipes, loot and tunables.</param>
/// <param name="Needs">Survival needs tuning.</param>
/// <param name="Food">Edible items.</param>
internal sealed record ServerGameData(GameEconomy Economy, NeedsConfig Needs, FoodCatalog Food)
{
    /// <summary>
    /// Documents a running server cannot swap yet (the building service is built once at boot): a reload that changes
    /// them is rejected, so clients never get a structure catalog the server is not running. Restart to apply them.
    /// </summary>
    public static readonly FrozenSet<string> RestartRequiredDocuments = FrozenSet.ToFrozenSet(["structures"], StringComparer.Ordinal);

    /// <summary>Builds and validates every swappable server domain from <paramref name="catalog"/>.</summary>
    /// <param name="catalog">Config snapshot.</param>
    /// <returns>The validated domains.</returns>
    /// <exception cref="InvalidDataException">Thrown when a document is missing, malformed or inconsistent.</exception>
    public static ServerGameData Load(ConfigCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        try
        {
            GameEconomy economy = GameEconomy.Load(
                Read(catalog, GameEconomy.ItemsFile),
                Read(catalog, GameEconomy.RecipesFile),
                Read(catalog, GameEconomy.EconomyFile),
                Read(catalog, GameEconomy.LootFile));
            NeedsConfig needs = NeedsConfig.LoadFromJson(Read(catalog, "needs.json"));
            FoodCatalog food = FoodCatalog.LoadFromJson(Read(catalog, "food.json"), needs.FoodMax);
            return new ServerGameData(economy, needs, food);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or ArgumentException or FormatException)
        {
            throw new InvalidDataException($"Server config is invalid: {ex.Message}", ex);
        }
    }

    /// <summary>Builds the building catalog from <c>structures.json</c> in <paramref name="catalog"/>.</summary>
    /// <param name="catalog">Config snapshot.</param>
    /// <returns>The structure catalog.</returns>
    public static StructureCatalog LoadStructures(ConfigCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return StructureCatalog.Parse(Read(catalog, "structures.json"));
    }

    /// <summary>
    /// Registers the reload step that rebuilds the domains from each new snapshot and swaps them into
    /// <paramref name="world"/> together with the served manifest (or rejects the reload, changing nothing).
    /// </summary>
    /// <param name="config">Config repository.</param>
    /// <param name="world">Running world.</param>
    public static void BindHotReload(ConfigRepository config, AuthoritativeWorld world)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(world);
        config.AddReloadStep((result, next) =>
        {
            foreach (string name in (IEnumerable<string>)[.. result.Changed, .. result.Removed])
            {
                if (RestartRequiredDocuments.Contains(name))
                {
                    throw new InvalidDataException($"'{name}.json' changed, and the running server cannot swap it; restart the server to apply it.");
                }
            }

            ServerGameData data = Load(next);
            return () => world.ApplyGameData(data.Economy, data.Needs, data.Food);
        });
    }

    private static string Read(ConfigCatalog catalog, string fileName) =>
        Encoding.UTF8.GetString(catalog.GetUtf8(Path.GetFileNameWithoutExtension(fileName)));
}
