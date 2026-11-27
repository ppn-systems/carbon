// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Carbon.Maps.Generators.Stages;

namespace Carbon.Maps.Props;

/// <summary>
/// How long a harvested natural prop takes to come back, from <c>resources/config/props.json</c> (ADR-040). The server
/// runs it (ADR-014). A prop type with no entry never comes back: stone, boulders and ore are permanent, and the infinite
/// world replaces them with exploration. Trees grow back through stages (<see cref="Trees"/>). Starter forage in the spawn clearing comes back after its own short delay, so a
/// new player with an empty bag can always gather a first tool.
/// </summary>
public sealed class PropRegrowthRules
{
    /// <summary>The config document name.</summary>
    public const string DocumentName = "props";

    /// <summary>Manifest resource name of the embedded <c>props.json</c>.</summary>
    public const string EmbeddedResourceName = "Carbon.Maps.Props.props.json";

    /// <summary>The longest delay a document may set (7 days).</summary>
    public const float MaxDelaySeconds = 7f * 24f * 3600f;

    private readonly FrozenDictionary<MapPropType, float> _regrowSeconds;

    private PropRegrowthRules(float spawnForageRespawnSeconds, float blockedRetrySeconds, FrozenDictionary<MapPropType, float> regrowSeconds, TreeGrowthRules trees)
    {
        this.Trees = trees;
        this.SpawnForageRespawnSeconds = spawnForageRespawnSeconds;
        this.BlockedRetrySeconds = blockedRetrySeconds;
        _regrowSeconds = regrowSeconds;
    }

    /// <summary>Gets how felled trees grow back through their stages (ADR-040 D1).</summary>
    public TreeGrowthRules Trees { get; }

    /// <summary>Gets the seconds starter forage inside the spawn clearing takes to come back.</summary>
    public float SpawnForageRespawnSeconds { get; }

    /// <summary>
    /// Gets the seconds a prop that is due waits before it tries again when its tile is blocked (a structure, a
    /// construction site or a player stands there): it never comes back inside them.
    /// </summary>
    public float BlockedRetrySeconds { get; }

    /// <summary>Gets <see cref="BlockedRetrySeconds"/> in world ticks (at least 1).</summary>
    /// <param name="tickRate">World ticks per second.</param>
    /// <returns>Ticks.</returns>
    public ulong BlockedRetryTicks(int tickRate) => Math.Max(1UL, (ulong)MathF.Round(this.BlockedRetrySeconds * Math.Max(1, tickRate)));

    /// <summary>Gets the regrowth delay in seconds per prop type; a type that is not listed never comes back.</summary>
    public IReadOnlyDictionary<MapPropType, float> RegrowSeconds => _regrowSeconds;

    /// <summary>
    /// Gets the seconds until a prop of <paramref name="type"/> destroyed on a tile comes back: the spawn forage delay
    /// for starter forage inside the clearing, else the type's regrowth delay, else 0 (never).
    /// </summary>
    /// <param name="type">The destroyed prop's type.</param>
    /// <param name="inSpawnClearing">Whether the tile lies in the spawn clearing (<see cref="SpawnClearingStage.IsInClearing"/>).</param>
    /// <returns>Seconds until it comes back; 0 when it never does.</returns>
    public float DelaySeconds(MapPropType type, bool inSpawnClearing)
    {
        if (inSpawnClearing && SpawnClearingStage.IsStarterForage(type))
        {
            return this.SpawnForageRespawnSeconds;
        }

        return _regrowSeconds.TryGetValue(type, out float seconds) ? seconds : 0f;
    }

    /// <summary>Converts <see cref="DelaySeconds"/> into world ticks (at least 1 when the prop comes back; 0 = never).</summary>
    /// <param name="type">The destroyed prop's type.</param>
    /// <param name="inSpawnClearing">Whether the tile lies in the spawn clearing.</param>
    /// <param name="tickRate">World ticks per second.</param>
    /// <returns>Ticks until it comes back; 0 when it never does.</returns>
    public ulong DelayTicks(MapPropType type, bool inSpawnClearing, int tickRate)
    {
        float seconds = this.DelaySeconds(type, inSpawnClearing);
        return seconds > 0f ? Math.Max(1UL, (ulong)MathF.Round(seconds * Math.Max(1, tickRate))) : 0UL;
    }

    /// <summary>Loads the copy of <c>props.json</c> embedded at build time (the server's default).</summary>
    /// <returns>The shipped rules.</returns>
    public static PropRegrowthRules LoadEmbedded()
    {
        using Stream stream = typeof(PropRegrowthRules).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException("Embedded default config 'props.json' is missing.");
        using StreamReader reader = new(stream);
        return LoadFromJson(reader.ReadToEnd());
    }

    /// <summary>Parses and validates <c>props.json</c>.</summary>
    /// <param name="json">The document.</param>
    /// <returns>The rules.</returns>
    /// <exception cref="InvalidDataException">The document is malformed, names an unknown or mineral prop, or a value is out of range.</exception>
    public static PropRegrowthRules LoadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            float forage = Required(root, "spawnForage", out JsonElement spawnForage) ? Number(spawnForage, "respawnSeconds") : 0f;
            Range(forage, 1f, 3600f, "spawnForage.respawnSeconds");
            float retry = Number(root, "blockedRetrySeconds");
            Range(retry, 1f, 3600f, "blockedRetrySeconds");

            Dictionary<MapPropType, float> regrow = [];
            if (Required(root, "regrowSeconds", out JsonElement table))
            {
                foreach (JsonProperty entry in table.EnumerateObject())
                {
                    MapPropType type = ParseType(entry.Name);
                    if (MapProp.IsTree(type))
                    {
                        throw new InvalidDataException(
                            $"props.json 'regrowSeconds.{entry.Name}': trees grow back through the 'trees' stages (ADR-040 D1).");
                    }

                    if (MapProp.CategoryOf(type) != PropCategory.Flora)
                    {
                        throw new InvalidDataException(
                            $"props.json 'regrowSeconds.{entry.Name}': only flora regrows; stone and ore never come back (ADR-040 D2).");
                    }

                    float seconds = entry.Value.GetSingle();
                    Range(seconds, 1f, MaxDelaySeconds, "regrowSeconds." + entry.Name);
                    if (!regrow.TryAdd(type, seconds))
                    {
                        throw new InvalidDataException($"props.json 'regrowSeconds' lists '{entry.Name}' twice.");
                    }
                }
            }

            _ = Required(root, "trees", out JsonElement trees);
            return new PropRegrowthRules(forage, retry, regrow.ToFrozenDictionary(), ParseTrees(trees));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"props.json is invalid: {ex.Message}", ex);
        }
    }

    private static TreeGrowthRules ParseTrees(JsonElement trees)
    {
        _ = Required(trees, "stump", out JsonElement stump);
        _ = Required(trees, "sapling", out JsonElement sapling);
        _ = Required(trees, "young", out JsonElement young);
        _ = Required(trees, "mature", out JsonElement mature);
        _ = Required(trees, "dig", out JsonElement dig);
        TreeStageRule stumpRule = Stage(stump, "trees.stump", "durability", 1f, 10_000f);
        TreeStageRule saplingRule = Stage(sapling, "trees.sapling", "durability", 1f, 10_000f);
        TreeStageRule youngRule = Stage(young, "trees.young", "durabilityShare", 0.05f, 1f);
        int matureYield = Integer(mature, "yield", 0, 99, "trees.mature.yield");
        float matureChance = Number(mature, "saplingChance");
        Range(matureChance, 0f, 1f, "trees.mature.saplingChance");
        float chance = Number(stump, "saplingChance");
        Range(chance, 0f, 1f, "trees.stump.saplingChance");
        int defer = Integer(trees, "deferWithinTiles", 0, 64, "trees.deferWithinTiles");
        float bareHand = Number(dig, "bareHandDamage");
        Range(bareHand, 0.1f, 10_000f, "trees.dig.bareHandDamage");
        float axe = Number(dig, "axeEfficiency");
        Range(axe, 0f, 1f, "trees.dig.axeEfficiency");
        return new TreeGrowthRules(stumpRule, saplingRule, youngRule, matureYield, matureChance, chance, defer, bareHand, axe);
    }

    private static TreeStageRule Stage(JsonElement stage, string name, string durabilityKey, float minDurability, float maxDurability)
    {
        float seconds = Number(stage, "seconds");
        Range(seconds, 1f, MaxDelaySeconds, name + ".seconds");
        float durability = Number(stage, durabilityKey);
        Range(durability, minDurability, maxDurability, name + "." + durabilityKey);
        return new TreeStageRule(seconds, durability, Integer(stage, "yield", 0, 99, name + ".yield"));
    }

    private static int Integer(JsonElement parent, string key, int min, int max, string name)
    {
        int value = parent.TryGetProperty(key, out JsonElement element)
            ? element.GetInt32()
            : throw new InvalidDataException($"props.json is missing '{name}'.");
        return value >= min && value <= max
            ? value
            : throw new InvalidDataException($"props.json '{name}' = {value} is outside [{min}, {max}].");
    }

    private static MapPropType ParseType(string name)
    {
        // Enum.TryParse also accepts numbers ("17"); names only, so a renumbered enum cannot change what a key means.
        if (name.Length == 0 || !char.IsLetter(name[0]) ||
            !Enum.TryParse(name, ignoreCase: true, out MapPropType type) || type == MapPropType.None)
        {
            throw new InvalidDataException($"props.json names unknown prop type '{name}'.");
        }

        return type;
    }

    private static bool Required(JsonElement parent, string name, out JsonElement value) =>
        parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object
            ? true
            : throw new InvalidDataException($"props.json is missing object '{name}'.");

    private static float Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out JsonElement value)
            ? value.GetSingle()
            : throw new InvalidDataException($"props.json is missing '{name}'.");

    private static void Range(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
        {
            throw new InvalidDataException($"props.json '{name}' = {value} is outside [{min}, {max}].");
        }
    }
}
