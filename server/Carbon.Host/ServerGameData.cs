// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Carbon.Application.World;
using Carbon.Combat.Statuses.Config;
using Carbon.Configuration.Catalog;
using Carbon.Creatures;
using Carbon.Creatures.Definitions;
using Carbon.Economy;
using Carbon.Items.Definitions;
using Carbon.Items.Progression;
using Carbon.Items.Races;
using Carbon.Maps.Building;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Props;
using Carbon.Server.Configuration;
using Carbon.Survival.Config;
using Carbon.Survival.Food;
using Carbon.Survival.Stamina;

namespace Carbon.Host;

/// <summary>
/// The server domains built from one config snapshot (ADR-014). Boot and every hot reload build them the same way,
/// so the server always runs exactly the documents it serves.
/// </summary>
/// <param name="Economy">Items, recipes, loot and tunables.</param>
/// <param name="Needs">Survival needs tuning.</param>
/// <param name="Food">Edible items.</param>
/// <param name="Progression">Character progression tuning (ADR-021).</param>
/// <param name="StatusEffects">Status effect definitions and monster procs (ADR-029).</param>
/// <param name="Stamina">Stamina tuning (ADR-033).</param>
/// <param name="Water">Water movement and oxygen rules (ADR-032).</param>
/// <param name="Props">Natural prop regrowth rules (ADR-040).</param>
/// <param name="Races">Playable races, bodies and appearance options (ADR-042).</param>
internal sealed record ServerGameData(
    GameEconomy Economy,
    NeedsConfig Needs,
    FoodCatalog Food,
    ProgressionConfig Progression,
    StatusEffectCatalog StatusEffects,
    StaminaConfig Stamina,
    WaterRules Water,
    PropRegrowthRules Props,
    RaceCatalog Races)
{
    /// <summary>
    /// Documents a running server cannot swap yet, because the building service and the creature systems are built once
    /// at boot. A reload that changes them is rejected, so clients never get data the server is not running.
    /// Restart the server to apply them.
    /// </summary>
    public static readonly FrozenSet<string> RestartRequiredDocuments =
        FrozenSet.ToFrozenSet(["structures", "monsters", "spawning"], StringComparer.Ordinal);

    /// <summary>Builds and validates every swappable server domain from <paramref name="catalog"/>.</summary>
    /// <param name="catalog">Config snapshot.</param>
    /// <returns>The validated domains.</returns>
    /// <exception cref="InvalidDataException">Thrown when a document is missing, malformed or inconsistent.</exception>
    public static ServerGameData Load(ConfigCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        try
        {
            GameEconomy economy = GameEconomy.Load(catalog);
            NeedsConfig needs = NeedsConfig.LoadFromJson(Read(catalog, "needs.json"));
            FoodCatalog food = FoodCatalog.LoadFromJson(Read(catalog, "food.json"), needs.FoodMax);
            ProgressionConfig progression = ProgressionConfig.LoadFromJson(Read(catalog, "progression.json"));
            StatusEffectCatalog statusEffects = StatusEffectCatalog.LoadFromJson(Read(catalog, "status_effects.json"));
            RequireKnownProcMonsters(statusEffects, LoadCreatures(catalog).Catalog);
            StaminaConfig stamina = StaminaConfig.LoadFromJson(Read(catalog, "stamina.json"));
            RequireKnownItemCodes(stamina, economy);
            WaterRules water = WaterRules.LoadFromJson(Read(catalog, WaterRules.DocumentName + ".json"));
            if (!statusEffects.TryGetByKey(water.DrowningEffect, out _))
            {
                throw new InvalidDataException($"water.json drowningEffect '{water.DrowningEffect}' is not in status_effects.json.");
            }

            PropRegrowthRules props = PropRegrowthRules.LoadFromJson(Read(catalog, PropRegrowthRules.DocumentName + ".json"));
            RaceCatalog races = RaceCatalog.LoadFromJson(Read(catalog, RaceCatalog.DocumentName + ".json"));
            return new ServerGameData(economy, needs, food, progression, statusEffects, stamina, water, props, races);
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

    /// <summary>Builds the monster catalog and spawning rules from <c>monsters.json</c> and <c>spawning.json</c>.</summary>
    /// <param name="catalog">Config snapshot.</param>
    /// <returns>The creature content.</returns>
    public static CreatureContent LoadCreatures(ConfigCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return CreatureContent.FromJson(Read(catalog, CreatureContent.MonstersFileName), Read(catalog, CreatureContent.SpawningFileName));
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
            data.Races.RequireCompatibleWith(world.Races);
            // Item stats and recipes (GameEconomy.ItemRules) swap with the economy, matching what clients re-sync.
            return () => world.ApplyGameData(data.Economy, data.Needs, data.Food, data.Progression, data.StatusEffects, data.Stamina, data.Water, data.Props, data.Races);
        });
    }

    /// <summary>Rejects a monster proc that names a monster missing from <c>monsters.json</c> (ADR-029 D1).</summary>
    /// <param name="statusEffects">Status effect catalog.</param>
    /// <param name="monsters">Monster catalog.</param>
    /// <exception cref="InvalidDataException">Thrown when a proc names an unknown monster.</exception>
    internal static void RequireKnownProcMonsters(StatusEffectCatalog statusEffects, MonsterCatalog monsters)
    {
        ArgumentNullException.ThrowIfNull(statusEffects);
        ArgumentNullException.ThrowIfNull(monsters);
        foreach (MonsterProcEntry proc in statusEffects.MonsterProcs)
        {
            if (!monsters.TryGetIndex(proc.Monster, out _))
            {
                throw new InvalidDataException($"status_effects.json monster proc names unknown monster '{proc.Monster}'.");
            }
        }
    }

    /// <summary>Rejects <c>itemSwingCosts</c> entries that name no item, so a typo cannot silently fall back to a default.</summary>
    private static void RequireKnownItemCodes(StaminaConfig stamina, GameEconomy economy)
    {
        HashSet<string> codes = new(StringComparer.Ordinal);
        foreach (ItemArchetype item in economy.ItemRules.Items.All)
        {
            _ = codes.Add(item.Code);
        }

        foreach (string code in stamina.ItemSwingCosts.Keys)
        {
            if (!codes.Contains(code))
            {
                throw new InvalidDataException($"stamina.json 'itemSwingCosts' names unknown item '{code}'.");
            }
        }
    }

    private static string Read(ConfigCatalog catalog, string fileName) =>
        Encoding.UTF8.GetString(catalog.GetUtf8(Path.GetFileNameWithoutExtension(fileName)));
}
