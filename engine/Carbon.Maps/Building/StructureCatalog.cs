// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Carbon.Maps.Building;

/// <summary>
/// Read-only catalog of <see cref="StructureDefinition"/>s and global construction rules, loaded from
/// <c>resources/config/structures.json</c>. Parsing uses <see cref="JsonDocument"/> so it stays Native AOT safe.
/// </summary>
public sealed class StructureCatalog
{
    private readonly Dictionary<StructureType, StructureDefinition> _byType;
    private readonly Dictionary<string, StructureDefinition> _byId;

    private StructureCatalog(List<StructureDefinition> definitions, BuildingRules rules)
    {
        _byType = new Dictionary<StructureType, StructureDefinition>(definitions.Count);
        _byId = new Dictionary<string, StructureDefinition>(definitions.Count, StringComparer.Ordinal);
        foreach (StructureDefinition definition in definitions)
        {
            if (!_byType.TryAdd(definition.Type, definition))
            {
                throw new FormatException($"Structure type {definition.Type} is defined more than once.");
            }

            if (!_byId.TryAdd(definition.Id, definition))
            {
                throw new FormatException($"Structure id '{definition.Id}' is defined more than once.");
            }
        }

        this.Definitions = definitions.AsReadOnly();
        this.Rules = rules;
    }

    /// <summary>Gets every definition in file order.</summary>
    public IReadOnlyList<StructureDefinition> Definitions { get; }

    /// <summary>Gets the global construction rules (refund fractions, reach).</summary>
    public BuildingRules Rules { get; }

    /// <summary>Looks up a definition by structure type.</summary>
    /// <param name="type">The structure type.</param>
    /// <param name="definition">The definition, when found.</param>
    /// <returns><c>true</c> when the type is defined.</returns>
    public bool TryGet(StructureType type, out StructureDefinition definition) =>
        _byType.TryGetValue(type, out definition!);

    /// <summary>Looks up a definition by its string id.</summary>
    /// <param name="id">The structure id.</param>
    /// <param name="definition">The definition, when found.</param>
    /// <returns><c>true</c> when the id is defined.</returns>
    public bool TryGet(string id, out StructureDefinition definition) =>
        _byId.TryGetValue(id, out definition!);

    /// <summary>Loads a catalog from a JSON file.</summary>
    /// <param name="path">Path to <c>structures.json</c>.</param>
    /// <returns>The parsed catalog.</returns>
    public static StructureCatalog LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parses a catalog from JSON text.</summary>
    /// <param name="json">The JSON document.</param>
    /// <returns>The parsed catalog.</returns>
    /// <exception cref="FormatException">The document is malformed or references unknown enum values.</exception>
    public static StructureCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        JsonElement root = document.RootElement;

        BuildingRules rules = new()
        {
            DeconstructRefundFraction = ReadFraction(root, "deconstructRefundFraction", BuildingRules.Default.DeconstructRefundFraction),
            DestroyedDropFraction = ReadFraction(root, "destroyedDropFraction", BuildingRules.Default.DestroyedDropFraction),
            DeconstructWorkFraction = ReadFraction(root, "deconstructWorkFraction", BuildingRules.Default.DeconstructWorkFraction),
            BuilderReachTiles = ReadInt(root, "builderReachTiles", BuildingRules.Default.BuilderReachTiles),
            PlacementReachTiles = ReadInt(root, "placementReachTiles", BuildingRules.Default.PlacementReachTiles)
        };

        if (!root.TryGetProperty("structures", out JsonElement structures) || structures.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("structures.json must contain a 'structures' array.");
        }

        List<StructureDefinition> definitions = new(structures.GetArrayLength());
        foreach (JsonElement element in structures.EnumerateArray())
        {
            definitions.Add(ParseDefinition(element));
        }

        return new StructureCatalog(definitions, rules);
    }

    private static StructureDefinition ParseDefinition(JsonElement element)
    {
        string id = ReadRequiredString(element, "id");
        StructureType type = ReadEnum<StructureType>(element, "type", id);
        if (type == StructureType.None)
        {
            throw new FormatException($"Structure '{id}' cannot use type None.");
        }

        List<MaterialCost> cost = [];
        if (element.TryGetProperty("cost", out JsonElement costArray))
        {
            foreach (JsonElement entry in costArray.EnumerateArray())
            {
                string item = ReadRequiredString(entry, "item");
                int quantity = ReadInt(entry, "qty", 0);
                if (quantity <= 0)
                {
                    throw new FormatException($"Structure '{id}' has a non-positive quantity for '{item}'.");
                }

                cost.Add(new MaterialCost(item, quantity));
            }
        }

        int width = 1;
        int height = 1;
        if (element.TryGetProperty("size", out JsonElement size))
        {
            if (size.ValueKind != JsonValueKind.Array || size.GetArrayLength() != 2)
            {
                throw new FormatException($"Structure '{id}' size must be [width, height].");
            }

            width = size[0].GetInt32();
            height = size[1].GetInt32();
        }

        if (width is < 1 or > ConstructionSite.MaxFootprintSide || height is < 1 or > ConstructionSite.MaxFootprintSide)
        {
            throw new FormatException($"Structure '{id}' footprint must be between 1 and {ConstructionSite.MaxFootprintSide} tiles per side.");
        }

        float work = ReadFloat(element, "work", 0f);
        float hitPoints = ReadFloat(element, "hitPoints", 0f);
        if (work <= 0f || hitPoints <= 0f)
        {
            throw new FormatException($"Structure '{id}' must have positive work and hitPoints.");
        }

        int toolTier = ReadInt(element, "toolTier", 0);
        if (toolTier is < 0 or > byte.MaxValue)
        {
            throw new FormatException($"Structure '{id}' toolTier is out of range.");
        }

        return new StructureDefinition
        {
            Id = id,
            Type = type,
            Category = ReadEnum<StructureCategory>(element, "category", id),
            Material = ReadEnum<StructureMaterial>(element, "material", id),
            Cost = cost.AsReadOnly(),
            WorkAmount = work,
            HitPoints = hitPoints,
            ToolTier = (byte)toolTier,
            Affordance = ReadEnum<TerrainAffordance>(element, "affordance", id),
            Width = width,
            Height = height,
            Rotatable = ReadBool(element, "rotatable"),
            RequiresFloor = ReadBool(element, "requiresFloor"),
            RequiresWallsOnSides = ReadBool(element, "requiresWallsOnSides"),
            MoveSpeedMultiplier = ReadFloat(element, "moveSpeed", 0f)
        };
    }

    private static TEnum ReadEnum<TEnum>(JsonElement element, string name, string id)
        where TEnum : struct, Enum
    {
        string raw = ReadRequiredString(element, name);
        if (!Enum.TryParse(raw, ignoreCase: false, out TEnum value) || !Enum.IsDefined(value) || char.IsDigit(raw[0]))
        {
            throw new FormatException($"Structure '{id}' has unknown {name} '{raw}'.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new FormatException($"Missing or empty string property '{name}'.");
        }

        return value.GetString()!;
    }

    private static int ReadInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetInt32() : fallback;

    private static float ReadFloat(JsonElement element, string name, float fallback) =>
        element.TryGetProperty(name, out JsonElement value) ? value.GetSingle() : fallback;

    private static bool ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.GetBoolean();

    private static float ReadFraction(JsonElement element, string name, float fallback)
    {
        float value = ReadFloat(element, name, fallback);
        if (value is < 0f or > 1f || float.IsNaN(value))
        {
            throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"'{name}' must be between 0 and 1 (was {value})."));
        }

        return value;
    }
}
