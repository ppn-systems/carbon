// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using Carbon.Maps.Building;

namespace Carbon.Maps.Settlements;

/// <summary>
/// The village building templates and style palettes of <c>resources/config/village_buildings.json</c>. The document is
/// world-generation data: it is compiled into this assembly and read by the village planner on the server and on every
/// client, so a change to it changes the generated world and needs a <c>GeneratorVersion</c> bump (ADR-025).
/// Parsing uses <see cref="JsonDocument"/> so it stays Native AOT safe.
/// </summary>
public sealed class VillageBuildingCatalog
{
    /// <summary>Manifest resource name of the embedded document.</summary>
    public const string EmbeddedResourceName = "Carbon.Maps.Settlements.village_buildings.json";

    private static readonly Lazy<VillageBuildingCatalog> s_default = new(LoadEmbedded);

    private readonly VillageStylePalette[] _palettes;

    private VillageBuildingCatalog(VillageStylePalette[] palettes, List<VillageBuildingTemplate> templates)
    {
        _palettes = palettes;
        this.Templates = templates.AsReadOnly();
    }

    /// <summary>Gets the catalog compiled into this assembly.</summary>
    public static VillageBuildingCatalog Default => s_default.Value;

    /// <summary>Gets every template in file order (the plan stores indices into this list).</summary>
    public ReadOnlyCollection<VillageBuildingTemplate> Templates { get; }

    /// <summary>Gets the palette of a style.</summary>
    /// <param name="style">The style.</param>
    /// <returns>The palette.</returns>
    public VillageStylePalette Palette(VillageStyle style) => _palettes[(int)style];

    /// <summary>
    /// Picks a template of <paramref name="role"/> buildable in <paramref name="style"/>, weighted by
    /// <see cref="VillageBuildingTemplate.Weight"/> and driven by <paramref name="hash"/>.
    /// </summary>
    /// <returns>The template index, or -1 when none exists.</returns>
    public int Pick(VillageBuildingRole role, VillageStyle style, uint hash)
    {
        int total = 0;
        for (int i = 0; i < this.Templates.Count; i++)
        {
            VillageBuildingTemplate t = this.Templates[i];
            if (t.Role == role && t.Supports(style))
            {
                total += t.Weight;
            }
        }

        if (total == 0)
        {
            return -1;
        }

        int roll = (int)(hash % (uint)total);
        for (int i = 0; i < this.Templates.Count; i++)
        {
            VillageBuildingTemplate t = this.Templates[i];
            if (t.Role == role && t.Supports(style))
            {
                roll -= t.Weight;
                if (roll < 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>Loads the catalog compiled into this assembly.</summary>
    /// <returns>The parsed catalog.</returns>
    public static VillageBuildingCatalog LoadEmbedded()
    {
        using Stream stream = typeof(VillageBuildingCatalog).Assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' is missing.");
        using StreamReader reader = new(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parses a catalog.</summary>
    /// <param name="json">The document.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="FormatException">The document is malformed or inconsistent.</exception>
    public static VillageBuildingCatalog Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("styles", out JsonElement styles) || styles.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("village_buildings.json must contain a 'styles' object.");
        }

        VillageStylePalette[] palettes = new VillageStylePalette[Enum.GetValues<VillageStyle>().Length];
        bool[] seen = new bool[palettes.Length];
        foreach (JsonProperty styleProperty in styles.EnumerateObject())
        {
            VillageStyle style = ParseStyle(styleProperty.Name);
            JsonElement p = styleProperty.Value;
            palettes[(int)style] = new VillageStylePalette(
                ReadType(p, "wall", StructureCategory.Wall, required: true),
                ReadType(p, "door", StructureCategory.Door, required: true),
                ReadType(p, "floor", StructureCategory.Floor, required: true),
                ReadType(p, "porch", StructureCategory.Floor, required: false));
            seen[(int)style] = true;
        }

        for (int i = 0; i < seen.Length; i++)
        {
            if (!seen[i])
            {
                throw new FormatException($"village_buildings.json has no palette for style '{(VillageStyle)i}'.");
            }
        }

        if (!root.TryGetProperty("buildings", out JsonElement buildings) || buildings.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("village_buildings.json must contain a 'buildings' array.");
        }

        List<VillageBuildingTemplate> templates = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (JsonElement b in buildings.EnumerateArray())
        {
            VillageBuildingTemplate template = ParseTemplate(b);
            if (!ids.Add(template.Id))
            {
                throw new FormatException($"Village building id '{template.Id}' is defined more than once.");
            }

            templates.Add(template);
        }

        return new VillageBuildingCatalog(palettes, templates);
    }

    private static VillageBuildingTemplate ParseTemplate(JsonElement b)
    {
        string id = ReadString(b, "id");
        VillageBuildingRole role = Enum.TryParse(ReadString(b, "role"), ignoreCase: true, out VillageBuildingRole r) && Enum.IsDefined(r)
            ? r
            : throw new FormatException($"Village building '{id}' has an unknown role.");
        int weight = b.TryGetProperty("weight", out JsonElement w) ? w.GetInt32() : 1;
        if (weight <= 0)
        {
            throw new FormatException($"Village building '{id}' weight must be positive.");
        }

        List<string> rows = [];
        foreach (JsonElement row in b.GetProperty("grid").EnumerateArray())
        {
            rows.Add(row.GetString() ?? string.Empty);
        }

        VillageBuildingTemplate.ValidateGrid(id, rows, out int width, out int height, out char[] cells);

        HashSet<VillageStyle> styles = [];
        if (b.TryGetProperty("styles", out JsonElement styleArray))
        {
            foreach (JsonElement s in styleArray.EnumerateArray())
            {
                _ = styles.Add(ParseStyle(s.GetString() ?? string.Empty));
            }
        }
        else
        {
            styles.UnionWith(Enum.GetValues<VillageStyle>());
        }

        List<VillageFurniture> furniture = [];
        if (b.TryGetProperty("furniture", out JsonElement furnitureArray))
        {
            foreach (JsonElement f in furnitureArray.EnumerateArray())
            {
                StructureType type = ParseStructureType(ReadString(f, "type"), id);
                JsonElement at = f.GetProperty("at");
                int fx = at[0].GetInt32();
                int fy = at[1].GetInt32();
                StructureRotation rotation = f.TryGetProperty("rotation", out JsonElement rot) &&
                    Enum.TryParse(rot.GetString(), ignoreCase: false, out StructureRotation parsed) && Enum.IsDefined(parsed)
                    ? parsed
                    : StructureRotation.North;
                char cell = fx >= 0 && fx < width && fy >= 0 && fy < height ? cells[(fy * width) + fx] : '?';
                if (cell is not (VillageBuildingTemplate.FloorCell or VillageBuildingTemplate.PorchCell or VillageBuildingTemplate.EmptyCell))
                {
                    throw new FormatException($"Village building '{id}' furniture {type} must stand on a floor, porch or open cell.");
                }

                furniture.Add(new VillageFurniture(type, fx, fy, rotation));
            }
        }

        (int X, int Y)? npc = null;
        if (b.TryGetProperty("npc", out JsonElement npcAt))
        {
            int nx = npcAt[0].GetInt32();
            int ny = npcAt[1].GetInt32();
            if (nx < 0 || nx >= width || ny < 0 || ny >= height ||
                cells[(ny * width) + nx] is VillageBuildingTemplate.WallCell or VillageBuildingTemplate.DoorCell)
            {
                throw new FormatException($"Village building '{id}' npc spot must be an open cell inside the grid.");
            }

            npc = (nx, ny);
        }

        return new VillageBuildingTemplate(id, role, weight, width, height, cells, furniture.AsReadOnly(), npc, styles);
    }

    private static VillageStyle ParseStyle(string name) =>
        Enum.TryParse(name, ignoreCase: true, out VillageStyle style) && Enum.IsDefined(style)
            ? style
            : throw new FormatException($"Unknown village style '{name}'.");

    private static StructureType ParseStructureType(string raw, string id) =>
        Enum.TryParse(raw, ignoreCase: false, out StructureType type) && Enum.IsDefined(type) && type != StructureType.None && !char.IsDigit(raw[0])
            ? type
            : throw new FormatException($"Village building '{id}' has unknown structure type '{raw}'.");

    private static StructureType ReadType(JsonElement palette, string name, StructureCategory category, bool required)
    {
        if (!palette.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return required ? throw new FormatException($"Village style palette is missing '{name}'.") : StructureType.None;
        }

        StructureType type = ParseStructureType(value.GetString() ?? string.Empty, name);
        StructureCategory actual = BuildingGrid.CreateBlueprint(type).Category;
        return actual == category
            ? type
            : throw new FormatException($"Village style '{name}' type {type} is a {actual}, expected {category}.");
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : throw new FormatException($"Missing or empty string property '{name}'.");
}
