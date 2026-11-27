// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Carbon.Graphics.Batching.Common;
using Carbon.Maps.Biomes;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// File-backed <see cref="IVectorArtRepository"/> implementation compiling external <c>.cvg.json</c> blueprints
/// into direct <c>O(1)</c> flat lookup tables using Native-AOT JSON serialization with zero raster image dependencies.
/// </summary>
public sealed class CarbonVectorArtLibrary : IVectorArtRepository
{
    private CompiledVectorRig?[] _characterTable = [];
    private CompiledVectorRig?[] _monsterTable = [];
    private CompiledVectorRig?[] _npcTable = [];
    private CompiledItemVectorArt?[] _itemTable = [];
    private readonly Dictionary<string, CompiledItemVectorArt> _uiIcons = new(StringComparer.Ordinal);
    private CompiledPropArt?[] _propTable = [];
    private CompiledBiomeArt?[] _biomeTable = [];
    private CompiledStructureArt?[] _structureTable = [];

    private int _characterCount;
    private int _monsterCount;
    private int _npcCount;
    private int _itemCount;
    private int _propCount;
    private CompiledPropArt? _treeSapling;
    private int _biomeCount;
    private int _structureCount;

    private CarbonVectorArtLibrary()
    {
    }

    /// <inheritdoc/>
    public int CharacterCount => _characterCount;

    /// <inheritdoc/>
    public int MonsterCount => _monsterCount;

    /// <inheritdoc/>
    public int NpcCount => _npcCount;

    /// <inheritdoc/>
    public int ItemCount => _itemCount;

    /// <inheritdoc/>
    public int PropCount => _propCount;

    /// <inheritdoc/>
    public int BiomeCount => _biomeCount;

    /// <inheritdoc/>
    public int StructureCount => _structureCount;

    /// <summary>
    /// Attempts to load external <c>.cvg.json</c> assets from <paramref name="hintDirectory"/> or discovered <c>resources/art</c>.
    /// </summary>
    public static CarbonVectorArtLibrary? LoadOrDefault(string? hintDirectory = null)
    {
        string? root = VectorArtValidator.TryFindArtRootDirectory(hintDirectory);
        return root is not null ? LoadFromDirectory(root) : null;
    }

    /// <summary>Loads and validates every blueprint declared in <c>catalog.cvg.json</c> under <paramref name="artRoot"/>.</summary>
    public static CarbonVectorArtLibrary LoadFromDirectory(string artRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artRoot);

        string catalogPath = Path.Combine(artRoot, "catalog.cvg.json");
        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException($"Vector catalog not found: {catalogPath}", catalogPath);
        }

        CvgCatalogDto catalog = JsonSerializer.Deserialize(
            File.ReadAllText(catalogPath),
            CvgJsonContext.Default.CvgCatalogDto)
            ?? throw new InvalidDataException("catalog.cvg.json deserialized to null.");

        CarbonVectorArtLibrary lib = new();
        Dictionary<string, CompiledVectorRig> compiledCache = new(StringComparer.OrdinalIgnoreCase);

        int maxCharId = 3;
        for (int i = 0; i < catalog.Characters.Count; i++)
        {
            if (catalog.Characters[i].Preset > maxCharId)
            {
                maxCharId = catalog.Characters[i].Preset;
            }
        }

        lib._characterTable = new CompiledVectorRig?[maxCharId + 1];
        for (int i = 0; i < catalog.Characters.Count; i++)
        {
            CvgCharacterCatalogDto entry = catalog.Characters[i];
            string fullPath = Path.GetFullPath(Path.Combine(artRoot, entry.File));
            if (!compiledCache.TryGetValue(fullPath, out CompiledVectorRig? compiled))
            {
                compiled = CompileRig(
                    fullPath,
                    entry.Title,
                    "ADVENTURER",
                    1,
                    entry.AccentColor);
                compiledCache[fullPath] = compiled;
            }

            if (lib._characterTable[entry.Preset] is null)
            {
                lib._characterCount++;
            }

            lib._characterTable[entry.Preset] = compiled;
        }

        int maxMonsterId = 101;
        for (int i = 0; i < catalog.Monsters.Count; i++)
        {
            if (catalog.Monsters[i].ArchetypeId > maxMonsterId)
            {
                maxMonsterId = catalog.Monsters[i].ArchetypeId;
            }
        }

        lib._monsterTable = new CompiledVectorRig?[maxMonsterId + 1];
        for (int i = 0; i < catalog.Monsters.Count; i++)
        {
            CvgArchetypeCatalogDto entry = catalog.Monsters[i];
            if (lib._monsterTable[entry.ArchetypeId] is null)
            {
                lib._monsterCount++;
            }

            lib._monsterTable[entry.ArchetypeId] = CompileRig(
                Path.Combine(artRoot, entry.File),
                entry.Title,
                entry.Subtitle,
                entry.Level,
                entry.AccentColor,
                entry.Elite);
        }

        int maxNpcId = 201;
        for (int i = 0; i < catalog.Npcs.Count; i++)
        {
            if (catalog.Npcs[i].ArchetypeId > maxNpcId)
            {
                maxNpcId = catalog.Npcs[i].ArchetypeId;
            }
        }

        lib._npcTable = new CompiledVectorRig?[maxNpcId + 1];
        for (int i = 0; i < catalog.Npcs.Count; i++)
        {
            CvgArchetypeCatalogDto entry = catalog.Npcs[i];
            if (lib._npcTable[entry.ArchetypeId] is null)
            {
                lib._npcCount++;
            }

            lib._npcTable[entry.ArchetypeId] = CompileRig(
                Path.Combine(artRoot, entry.File),
                entry.Title,
                entry.Subtitle,
                1,
                entry.AccentColor);
        }

        uint maxItemId = 32;
        for (int i = 0; i < catalog.Items.Count; i++)
        {
            if (catalog.Items[i].ItemId > maxItemId)
            {
                maxItemId = catalog.Items[i].ItemId;
            }
        }

        lib._itemTable = new CompiledItemVectorArt?[maxItemId + 1];
        for (int i = 0; i < catalog.Items.Count; i++)
        {
            CvgItemCatalogDto entry = catalog.Items[i];
            if (lib._itemTable[entry.ItemId] is null)
            {
                lib._itemCount++;
            }

            lib._itemTable[entry.ItemId] = CompileItem(Path.Combine(artRoot, entry.File));
        }

        for (int i = 0; i < catalog.UiIcons.Count; i++)
        {
            CvgUiIconCatalogDto entry = catalog.UiIcons[i];
            lib._uiIcons[entry.Id] = CompileItem(Path.Combine(artRoot, entry.File));
        }

        if (catalog.TreeSapling is { File.Length: > 0 } sapling)
        {
            lib._treeSapling = CompileProp(Path.Combine(artRoot, sapling.File));
        }

        int maxPropId = 22;
        for (int i = 0; i < catalog.Props.Count; i++)
        {
            if (catalog.Props[i].PropType > maxPropId)
            {
                maxPropId = catalog.Props[i].PropType;
            }
        }

        lib._propTable = new CompiledPropArt?[maxPropId + 1];
        for (int i = 0; i < catalog.Props.Count; i++)
        {
            CvgPropCatalogDto entry = catalog.Props[i];
            if (lib._propTable[entry.PropType] is null)
            {
                lib._propCount++;
            }

            lib._propTable[entry.PropType] = CompileProp(Path.Combine(artRoot, entry.File));
        }

        int maxBiomeId = 13;
        for (int i = 0; i < catalog.Biomes.Count; i++)
        {
            if (catalog.Biomes[i].BiomeId > maxBiomeId)
            {
                maxBiomeId = catalog.Biomes[i].BiomeId;
            }
        }

        lib._biomeTable = new CompiledBiomeArt?[maxBiomeId + 1];
        for (int i = 0; i < catalog.Biomes.Count; i++)
        {
            CvgBiomeCatalogDto entry = catalog.Biomes[i];
            if (lib._biomeTable[entry.BiomeId] is null)
            {
                lib._biomeCount++;
            }

            lib._biomeTable[entry.BiomeId] = CompileBiome(Path.Combine(artRoot, entry.File));
        }

        int maxStructId = 8;
        for (int i = 0; i < catalog.Structures.Count; i++)
        {
            if (catalog.Structures[i].StructureType > maxStructId)
            {
                maxStructId = catalog.Structures[i].StructureType;
            }
        }

        lib._structureTable = new CompiledStructureArt?[maxStructId + 1];
        for (int i = 0; i < catalog.Structures.Count; i++)
        {
            CvgStructureCatalogDto entry = catalog.Structures[i];
            if (lib._structureTable[entry.StructureType] is null)
            {
                lib._structureCount++;
            }

            lib._structureTable[entry.StructureType] = CompileStructure(Path.Combine(artRoot, entry.File));
        }

        return lib;
    }

    /// <inheritdoc/>
    public bool TryGetCharacter(byte preset, [NotNullWhen(true)] out CompiledVectorRig? rig)
    {
        if (preset < _characterTable.Length && _characterTable[preset] is { } found)
        {
            rig = found;
            return true;
        }

        if (_characterTable.Length > 0 && _characterTable[0] is { } fallback)
        {
            rig = fallback;
            return true;
        }

        rig = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetMonster(ushort archetypeId, [NotNullWhen(true)] out CompiledVectorRig? rig)
    {
        if (archetypeId < _monsterTable.Length && _monsterTable[archetypeId] is { } found)
        {
            rig = found;
            return true;
        }

        if (_monsterTable.Length > 101 && _monsterTable[101] is { } fallback)
        {
            rig = fallback;
            return true;
        }

        rig = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetNpc(ushort archetypeId, [NotNullWhen(true)] out CompiledVectorRig? rig)
    {
        if (archetypeId < _npcTable.Length && _npcTable[archetypeId] is { } found)
        {
            rig = found;
            return true;
        }

        if (_npcTable.Length > 201 && _npcTable[201] is { } fallback)
        {
            rig = fallback;
            return true;
        }

        rig = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetUiIcon(string iconId, [NotNullWhen(true)] out CompiledItemVectorArt? icon)
    {
        ArgumentNullException.ThrowIfNull(iconId);
        return _uiIcons.TryGetValue(iconId, out icon);
    }

    /// <inheritdoc/>
    public bool TryGetItem(uint itemId, [NotNullWhen(true)] out CompiledItemVectorArt? item)
    {
        if (itemId < (uint)_itemTable.Length && _itemTable[itemId] is { } found)
        {
            item = found;
            return true;
        }

        item = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetTreeSapling([NotNullWhen(true)] out CompiledPropArt? sapling)
    {
        sapling = _treeSapling;
        return sapling is not null;
    }

    /// <inheritdoc/>
    public bool TryGetProp(byte propType, [NotNullWhen(true)] out CompiledPropArt? prop)
    {
        if (propType < _propTable.Length && _propTable[propType] is { } found)
        {
            prop = found;
            return true;
        }

        prop = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetBiome(byte biomeId, [NotNullWhen(true)] out CompiledBiomeArt? biome)
    {
        if (biomeId < _biomeTable.Length && _biomeTable[biomeId] is { } found)
        {
            biome = found;
            return true;
        }

        biome = null;
        return false;
    }

    /// <inheritdoc/>
    public bool TryGetStructure(byte structureType, [NotNullWhen(true)] out CompiledStructureArt? structureArt)
    {
        if (structureType < _structureTable.Length && _structureTable[structureType] is { } found)
        {
            structureArt = found;
            return true;
        }

        structureArt = null;
        return false;
    }

    /// <inheritdoc/>
    public string GetBiomeDisplayName(byte biomeId) =>
        this.TryGetBiome(biomeId, out CompiledBiomeArt? biome)
            ? biome.DisplayName
            : ((BiomeType)biomeId).ToString();

    private static CompiledVectorRig CompileRig(
        string filePath,
        string title,
        string subtitle,
        byte level,
        string fallbackAccentHex,
        bool isElite = false)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Vector rig blueprint missing: {filePath}", filePath);
        }

        CvgEntityRigDto dto = JsonSerializer.Deserialize(
            File.ReadAllText(filePath),
            CvgJsonContext.Default.CvgEntityRigDto)
            ?? throw new InvalidDataException($"Rig blueprint null: {filePath}");

        if (dto.Shapes.Count == 0 || dto.Animations.Count == 0)
        {
            throw new InvalidDataException($"Rig '{dto.Id}' must define non-empty shapes and animations in {filePath}");
        }

        CompiledVectorShape[] shapes = CompileShapes(dto.Shapes, dto.Palette, filePath);
        Dictionary<string, CompiledVectorAnimation> anims = CompileAnimations(dto.Animations);
        Vector2[] pivots = CompilePivots(dto.BonePivots, dto.DesignHeight);

        Vector2 southGrip = ResolveGrip(dto.WeaponGrip, "south", new Vector2(8f, -10f));
        Vector2 sideGrip = ResolveGrip(dto.WeaponGrip, "side", southGrip);
        Vector2 northGrip = ResolveGrip(dto.WeaponGrip, "north", new Vector2(8f, -14f));
        ColorRgba accent = ParseColor(!string.IsNullOrWhiteSpace(dto.AccentColor) ? dto.AccentColor : fallbackAccentHex, dto.Palette);

        return new CompiledVectorRig(
            dto.Id,
            title,
            subtitle,
            level,
            accent,
            dto.DesignHeight,
            dto.WorldHeight,
            dto.HeadTopOffset > 0f ? dto.HeadTopOffset : dto.WorldHeight * 0.9f,
            southGrip,
            sideGrip,
            northGrip,
            pivots,
            shapes,
            anims,
            dto.SupportsModularHuman)
        {
            IsElite = isElite,
            Markers = CompileMarkers(dto, accent),
        };
    }

    private static RigMarkers CompileMarkers(CvgEntityRigDto dto, ColorRgba accent)
    {
        CvgRigMarkersDto? m = dto.Markers;
        float headTop = dto.HeadTopOffset > 0f ? dto.HeadTopOffset : dto.WorldHeight * 0.9f;
        if (m is null)
        {
            return new RigMarkers([], [], new Vector2(0f, -headTop), accent);
        }

        Vector2[] eyes = [.. m.Eyes.Select(static p => new Vector2(p.X, p.Y))];
        Vector2[] eyesSide = [.. m.EyesSide.Select(static p => new Vector2(p.X, p.Y))];
        Vector2 crown = m.Crown is null ? new Vector2(0f, -headTop) : new Vector2(m.Crown.X, m.Crown.Y);
        ColorRgba glow = string.IsNullOrWhiteSpace(m.EyeGlowColor) ? accent : ParseColor(m.EyeGlowColor, dto.Palette);
        return new RigMarkers(eyes, eyesSide, crown, glow);
    }

    private static CompiledItemVectorArt CompileItem(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Item blueprint missing: {filePath}", filePath);
        }

        CvgItemBlueprintDto dto = JsonSerializer.Deserialize(
            File.ReadAllText(filePath),
            CvgJsonContext.Default.CvgItemBlueprintDto)
            ?? throw new InvalidDataException($"Item blueprint null: {filePath}");

        if (dto.Shapes.Count == 0)
        {
            throw new InvalidDataException($"Item '{dto.Id}' has no shapes in {filePath}");
        }

        CompiledVectorShape[] shapes = CompileShapes(dto.Shapes, dto.Palette, filePath);
        ColorRgba primaryArmor = dto.ArmorVisual is not null ? ParseColor(dto.ArmorVisual.PrimaryColor, dto.Palette) : default;
        ColorRgba secondaryArmor = dto.ArmorVisual is not null ? ParseColor(dto.ArmorVisual.SecondaryColor, dto.Palette) : default;
        ColorRgba trimArmor = dto.ArmorVisual is not null ? ParseColor(dto.ArmorVisual.TrimColor, dto.Palette) : default;

        return new CompiledItemVectorArt(
            dto.ItemId,
            dto.Id,
            dto.DisplayName,
            dto.Tier,
            dto.CategoryLabel,
            ParseColor(dto.AccentColor, dto.Palette),
            new Vector2(dto.GripX, dto.GripY),
            dto.RestAngleDeg * (MathF.PI / 180f),
            dto.WorldScale > 0f ? dto.WorldScale : 1f,
            shapes,
            dto.HeldVisible,
            primaryArmor,
            secondaryArmor,
            trimArmor);
    }

    private static CompiledPropArt CompileProp(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Prop blueprint missing: {filePath}", filePath);
        }

        CvgPropBlueprintDto dto = JsonSerializer.Deserialize(
            File.ReadAllText(filePath),
            CvgJsonContext.Default.CvgPropBlueprintDto)
            ?? throw new InvalidDataException($"Prop blueprint null: {filePath}");

        if (dto.GroundShapes.Count == 0)
        {
            throw new InvalidDataException($"Prop '{dto.Id}' must define non-empty groundShapes in {filePath}");
        }

        if (dto.HasCanopy && dto.CanopyShapes.Count == 0)
        {
            throw new InvalidDataException($"Prop '{dto.Id}' declares hasCanopy=true without canopyShapes in {filePath}");
        }

        int variantCount = Math.Max(1, dto.PaletteVariants.Count);
        CompiledVectorShape[][] groundVariants = new CompiledVectorShape[variantCount][];
        CompiledVectorShape[][] canopyVariants = new CompiledVectorShape[variantCount][];
        CompiledVectorShape[][] remnantVariants = new CompiledVectorShape[variantCount][];

        for (int v = 0; v < variantCount; v++)
        {
            Dictionary<string, string>? palette = dto.PaletteVariants.Count > 0 ? dto.PaletteVariants[v] : null;
            groundVariants[v] = CompileShapes(dto.GroundShapes, palette, filePath);
            canopyVariants[v] = dto.CanopyShapes.Count > 0 ? CompileShapes(dto.CanopyShapes, palette, filePath) : [];
            remnantVariants[v] = dto.RemnantShapes.Count > 0 ? CompileShapes(dto.RemnantShapes, palette, filePath) : [];
        }

        CompiledPropLight? light = dto.Light is not null
            ? new CompiledPropLight(
                new Vector2(dto.Light.OffsetX, dto.Light.OffsetY),
                dto.Light.Radius,
                ParseColor(dto.Light.Color, dto.PaletteVariants.Count > 0 ? dto.PaletteVariants[^1] : null),
                dto.Light.Intensity,
                dto.Light.RareOnly)
            : null;

        return new CompiledPropArt(
            dto.PropType,
            dto.Id,
            dto.DisplayName,
            dto.DesignRadius,
            dto.HasCanopy,
            dto.AllowMirror,
            dto.IsWoody,
            dto.IsIcy,
            dto.RareLastVariantChance,
            light,
            groundVariants,
            canopyVariants,
            remnantVariants);
    }

    private static CompiledBiomeArt CompileBiome(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Biome blueprint missing: {filePath}", filePath);
        }

        CvgBiomeBlueprintDto dto = JsonSerializer.Deserialize(
            File.ReadAllText(filePath),
            CvgJsonContext.Default.CvgBiomeBlueprintDto)
            ?? throw new InvalidDataException($"Biome blueprint null: {filePath}");

        if (string.IsNullOrWhiteSpace(dto.DisplayName))
        {
            throw new InvalidDataException($"Biome '{dto.Id}' must define a non-empty displayName in {filePath}");
        }

        if (dto.DetailVariants.Count == 0)
        {
            throw new InvalidDataException($"Biome '{dto.Id}' must define detailVariants in {filePath}");
        }

        CompiledVectorShape[][] variants = new CompiledVectorShape[dto.DetailVariants.Count][];
        for (int v = 0; v < dto.DetailVariants.Count; v++)
        {
            CvgBiomeDetailVariantDto dv = dto.DetailVariants[v];
            if (dv.Shapes.Count == 0)
            {
                throw new InvalidDataException($"Biome '{dto.Id}' detailVariant[{v}] has no shapes in {filePath}");
            }

            variants[v] = CompileShapes(dv.Shapes, dto.Palette, filePath);
        }

        return new CompiledBiomeArt(
            dto.BiomeId,
            dto.Id,
            dto.DisplayName,
            ParseColor(dto.GroundColor, dto.Palette),
            ParseColor(dto.MinimapColor, dto.Palette),
            ParseColor(dto.CliffBase, dto.Palette),
            ParseColor(dto.CliffStrata, dto.Palette),
            ParseColor(dto.CliffHighlight, dto.Palette),
            ParseColor(dto.CliffShadow, dto.Palette),
            dto.DetailChancePercent,
            variants,
            CompileSurface(dto, filePath),
            CompileDecalLayers(dto, filePath));
    }

    private static BiomeSurfaceStyle? CompileSurface(CvgBiomeBlueprintDto dto, string filePath)
    {
        if (dto.Surface is not { } surface)
        {
            return null;
        }

        if (!Enum.TryParse(surface.Pattern, ignoreCase: true, out BiomeSurfacePattern pattern) || !Enum.IsDefined(pattern))
        {
            throw new InvalidDataException($"Biome '{dto.Id}' has unknown surface pattern '{surface.Pattern}' in {filePath}");
        }

        if (surface.Colors.Count == 0)
        {
            throw new InvalidDataException($"Biome '{dto.Id}' surface must define at least one colour in {filePath}");
        }

        Vector3[] colors = new Vector3[surface.Colors.Count];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = ToVector(ParseColor(surface.Colors[i], dto.Palette));
        }

        Vector3 accent = surface.Accent is { Length: > 0 } a ? ToVector(ParseColor(a, dto.Palette)) : colors[0] * 0.8f;
        return new BiomeSurfaceStyle(
            pattern,
            colors,
            accent,
            surface.Scale > 0f ? surface.Scale : 1f,
            Math.Clamp(surface.Variation, 0f, 0.5f),
            Math.Clamp(surface.Grain, 0f, 0.5f));

        static Vector3 ToVector(ColorRgba c) => new(c.R, c.G, c.B);
    }

    private static BiomeDecalLayer[] CompileDecalLayers(CvgBiomeBlueprintDto dto, string filePath)
    {
        if (dto.Decals.Count == 0)
        {
            return BiomeDecalLayer.None;
        }

        BiomeDecalLayer[] layers = new BiomeDecalLayer[dto.Decals.Count];
        for (int l = 0; l < layers.Length; l++)
        {
            CvgBiomeDecalLayerDto layer = dto.Decals[l];
            if (layer.Variants.Count == 0)
            {
                throw new InvalidDataException($"Biome '{dto.Id}' decal layer '{layer.Id}' has no variants in {filePath}");
            }

            if (!Enum.TryParse(layer.Placement, ignoreCase: true, out BiomeDecalPlacement placement) || !Enum.IsDefined(placement))
            {
                throw new InvalidDataException($"Biome '{dto.Id}' decal layer '{layer.Id}' has unknown placement '{layer.Placement}' in {filePath}");
            }

            BiomeDecalVariant[] variants = new BiomeDecalVariant[layer.Variants.Count];
            for (int v = 0; v < variants.Length; v++)
            {
                if (layer.Variants[v].Shapes.Count == 0)
                {
                    throw new InvalidDataException($"Biome '{dto.Id}' decal layer '{layer.Id}' variant {v} has no shapes in {filePath}");
                }

                variants[v] = BiomeDecalLayer.Measure(CompileShapes(layer.Variants[v].Shapes, dto.Palette, filePath));
            }

            layers[l] = new BiomeDecalLayer(
                layer.Id,
                Math.Clamp(layer.Chance, 0f, 1f),
                Math.Clamp(layer.MaxCount, 1, 8),
                placement,
                layer.Jitter,
                variants);
        }

        return layers;
    }

    private static CompiledStructureArt CompileStructure(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Structure blueprint missing: {filePath}", filePath);
        }

        CvgStructureBlueprintDto dto = JsonSerializer.Deserialize(
            File.ReadAllText(filePath),
            CvgJsonContext.Default.CvgStructureBlueprintDto)
            ?? throw new InvalidDataException($"Structure blueprint null: {filePath}");

        if (dto.Shapes.Count == 0)
        {
            throw new InvalidDataException($"Structure '{dto.Id}' must define non-empty shapes in {filePath}");
        }

        CompiledVectorShape[] shapes = CompileShapes(dto.Shapes, dto.Palette, filePath);
        CompiledVectorShape[] openShapes = dto.OpenShapes.Count > 0 ? CompileShapes(dto.OpenShapes, dto.Palette, filePath) : [];

        int footW = dto.Size.Count == 2 ? dto.Size[0] : 1;
        int footH = dto.Size.Count == 2 ? dto.Size[1] : 1;
        if (footW is < 1 or > 4 || footH is < 1 or > 4)
        {
            throw new InvalidDataException($"Structure '{dto.Id}' size must be [1..4, 1..4] in {filePath}");
        }

        return new CompiledStructureArt(
            dto.StructureType,
            dto.Id,
            dto.DisplayName,
            shapes,
            openShapes,
            footW,
            footH);
    }

    private static CompiledVectorShape[] CompileShapes(
        List<CvgShapeDto> list,
        Dictionary<string, string>? palette,
        string filePath)
    {
        CompiledVectorShape[] compiled = new CompiledVectorShape[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            CvgShapeDto s = list[i];
            VectorShapeKind kind = ParseShapeKind(s.Kind, filePath);
            VectorBoneId bone = ParseBone(s.Bone);
            VectorFacingFilter facing = ParseFacing(s.Facing);

            Vector2[]? pts = null;
            if (kind == VectorShapeKind.Polygon)
            {
                if (s.Points is null || s.Points.Count < 3 || s.Points.Count > 12)
                {
                    throw new InvalidDataException($"Polygon must have 3..12 vertices in {filePath}");
                }

                pts = new Vector2[s.Points.Count];
                for (int v = 0; v < s.Points.Count; v++)
                {
                    pts[v] = new Vector2(s.Points[v][0], s.Points[v][1]);
                }
            }

            Vector2 p0 = new(s.X, s.Y);
            Vector2 p1 = kind switch
            {
                VectorShapeKind.Rect => new Vector2(s.W, s.H),
                VectorShapeKind.Capsule or VectorShapeKind.Line => new Vector2(s.X2, s.Y2),
                VectorShapeKind.Ring => new Vector2(s.Rx, s.Ry > 0f ? s.Ry : s.Rx * 0.45f),
                VectorShapeKind.Ellipse or VectorShapeKind.Polygon or _ => new Vector2(s.Rx, s.Ry),
            };

            bool hasOutline = !string.IsNullOrWhiteSpace(s.OutlineColor);
            ColorRgba outline = hasOutline ? ParseColor(s.OutlineColor!, palette) : default;
            VectorPaletteRole role = ParseRole(s.Role);

            compiled[i] = new CompiledVectorShape(
                kind,
                bone,
                facing,
                p0,
                p1,
                s.RotDeg * (MathF.PI / 180f),
                s.Thickness > 0f ? s.Thickness : 1.5f,
                ParseColor(s.Color, palette),
                hasOutline,
                outline,
                s.OutlineThickness > 0f ? s.OutlineThickness : 1.1f,
                pts,
                role);
        }

        return compiled;
    }

    private static Dictionary<string, CompiledVectorAnimation> CompileAnimations(Dictionary<string, CvgAnimDto> raw)
    {
        Dictionary<string, CompiledVectorAnimation> dict = new(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, CvgAnimDto> kvp in raw)
        {
            CvgAnimDto src = kvp.Value;
            CompiledKeyframe[] frames = new CompiledKeyframe[src.Tracks.Count];
            for (int i = 0; i < src.Tracks.Count; i++)
            {
                CvgKeyframeDto kfDto = src.Tracks[i];
                BonePose[] bones = new BonePose[(int)VectorBoneId.Count];
                for (int b = 0; b < bones.Length; b++)
                {
                    bones[b] = BonePose.Identity;
                }

                foreach (KeyValuePair<string, CvgBonePoseDto> bk in kfDto.Bones)
                {
                    int idx = (int)ParseBone(bk.Key);
                    CvgBonePoseDto p = bk.Value;
                    bones[idx] = new BonePose
                    {
                        Dx = p.Dx,
                        Dy = p.Dy,
                        RotRad = p.RotDeg * (MathF.PI / 180f),
                        ScaleX = p.ScaleX ?? 1f,
                        ScaleY = p.ScaleY ?? 1f,
                    };
                }

                frames[i] = new CompiledKeyframe(kfDto.T, bones);
            }

            Array.Sort(frames, static (a, b) => a.Time.CompareTo(b.Time));
            AttackPhaseMarks? marks = src.PhaseMarks is null
                ? null
                : AttackPhaseMarks.Create(src.PhaseMarks.WindupEnd, src.PhaseMarks.TravelEnd, src.PhaseMarks.ActiveEnd);
            dict[kvp.Key] = new CompiledVectorAnimation(src.Duration, src.Loop, frames, marks);
        }

        return dict;
    }

    private static Vector2[] CompilePivots(Dictionary<string, CvgPointDto>? raw, float designHeight)
    {
        float h = MathF.Max(16f, designHeight);
        Vector2[] pivots = new Vector2[(int)VectorBoneId.Count];
        pivots[(int)VectorBoneId.Root] = Vector2.Zero;
        pivots[(int)VectorBoneId.Cape] = new Vector2(0f, -h * 0.52f);
        pivots[(int)VectorBoneId.Tail] = new Vector2(0f, -h * 0.28f);
        pivots[(int)VectorBoneId.LegLeft] = new Vector2(-h * 0.08f, -h * 0.28f);
        pivots[(int)VectorBoneId.LegRight] = new Vector2(h * 0.08f, -h * 0.28f);
        pivots[(int)VectorBoneId.Body] = new Vector2(0f, -h * 0.42f);
        pivots[(int)VectorBoneId.ArmBack] = new Vector2(-h * 0.16f, -h * 0.56f);
        pivots[(int)VectorBoneId.ArmFront] = new Vector2(h * 0.16f, -h * 0.56f);
        pivots[(int)VectorBoneId.Head] = new Vector2(0f, -h * 0.72f);
        pivots[(int)VectorBoneId.Headgear] = new Vector2(0f, -h * 0.76f);
        pivots[(int)VectorBoneId.Aura] = new Vector2(0f, -h * 0.48f);

        if (raw is not null)
        {
            foreach (KeyValuePair<string, CvgPointDto> kvp in raw)
            {
                if (kvp.Value is not null)
                {
                    int idx = (int)ParseBone(kvp.Key);
                    pivots[idx] = new Vector2(kvp.Value.X, kvp.Value.Y);
                }
            }
        }

        return pivots;
    }

    private static Vector2 ResolveGrip(Dictionary<string, CvgPointDto>? grips, string key, Vector2 fallback)
    {
        if (grips is not null && grips.TryGetValue(key, out CvgPointDto? pt) && pt is not null)
        {
            return new Vector2(pt.X, pt.Y);
        }

        return fallback;
    }

    private static VectorShapeKind ParseShapeKind(string kind, string filePath) => kind.ToLowerInvariant() switch
    {
        "ellipse" or "circle" => VectorShapeKind.Ellipse,
        "rect" => VectorShapeKind.Rect,
        "capsule" => VectorShapeKind.Capsule,
        "polygon" or "triangle" => VectorShapeKind.Polygon,
        "line" => VectorShapeKind.Line,
        "ring" => VectorShapeKind.Ring,
        _ => throw new InvalidDataException($"Unknown shape kind '{kind}' in {filePath}"),
    };

    private static VectorBoneId ParseBone(string bone) => bone.ToLowerInvariant() switch
    {
        "root" => VectorBoneId.Root,
        "cape" => VectorBoneId.Cape,
        "tail" => VectorBoneId.Tail,
        "legleft" => VectorBoneId.LegLeft,
        "legright" => VectorBoneId.LegRight,
        "body" => VectorBoneId.Body,
        "armback" => VectorBoneId.ArmBack,
        "head" => VectorBoneId.Head,
        "headgear" => VectorBoneId.Headgear,
        "armfront" => VectorBoneId.ArmFront,
        "aura" => VectorBoneId.Aura,
        _ => VectorBoneId.Body,
    };

    private static VectorFacingFilter ParseFacing(string facing) => facing.ToLowerInvariant() switch
    {
        "south" => VectorFacingFilter.South,
        "side" => VectorFacingFilter.Side,
        "north" => VectorFacingFilter.North,
        "notnorth" => VectorFacingFilter.NotNorth,
        "any" or _ => VectorFacingFilter.Any,
    };

    private static VectorPaletteRole ParseRole(string? role) =>
        string.IsNullOrWhiteSpace(role)
            ? VectorPaletteRole.None
            : role.ToLowerInvariant() switch
            {
                "skin" => VectorPaletteRole.Skin,
                "skinshadow" or "skin_shadow" => VectorPaletteRole.SkinShadow,
                "hair" => VectorPaletteRole.Hair,
                "eye" => VectorPaletteRole.Eye,
                _ => VectorPaletteRole.None,
            };

    private static ColorRgba ParseColor(string raw, Dictionary<string, string>? palette)
    {
        string value = raw;
        if (value.StartsWith('$') && palette is not null && palette.TryGetValue(value[1..], out string? mapped))
        {
            value = mapped;
        }

        ReadOnlySpan<char> hex = value.StartsWith('#') ? value.AsSpan(1) : value.AsSpan();
        if (hex.Length == 6)
        {
            byte r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(hex.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(hex.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new ColorRgba(r, g, b, 255);
        }

        if (hex.Length == 8)
        {
            byte r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(hex.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(hex.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte a = byte.Parse(hex.Slice(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new ColorRgba(r, g, b, a);
        }

        return ColorRgba.White;
    }
}
