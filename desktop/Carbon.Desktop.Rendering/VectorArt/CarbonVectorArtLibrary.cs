// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// File-backed <see cref="IVectorArtRepository"/> implementation compiling external <c>.cvg.json</c> blueprints
/// using Native-AOT JSON serialization with zero raster image dependencies.
/// </summary>
public sealed class CarbonVectorArtLibrary : IVectorArtRepository
{
    private readonly Dictionary<byte, CompiledVectorRig> _characters = [];
    private readonly Dictionary<ushort, CompiledVectorRig> _monsters = [];
    private readonly Dictionary<ushort, CompiledVectorRig> _npcs = [];
    private readonly Dictionary<uint, CompiledItemVectorArt> _items = [];

    private CarbonVectorArtLibrary()
    {
    }

    /// <inheritdoc/>
    public int CharacterCount => _characters.Count;

    /// <inheritdoc/>
    public int MonsterCount => _monsters.Count;

    /// <inheritdoc/>
    public int NpcCount => _npcs.Count;

    /// <inheritdoc/>
    public int ItemCount => _items.Count;

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

        for (int i = 0; i < catalog.Characters.Count; i++)
        {
            CvgCharacterCatalogDto entry = catalog.Characters[i];
            lib._characters[entry.Preset] = CompileRig(
                Path.Combine(artRoot, entry.File),
                entry.Title,
                "ADVENTURER",
                1,
                entry.AccentColor);
        }

        for (int i = 0; i < catalog.Monsters.Count; i++)
        {
            CvgArchetypeCatalogDto entry = catalog.Monsters[i];
            lib._monsters[entry.ArchetypeId] = CompileRig(
                Path.Combine(artRoot, entry.File),
                entry.Title,
                entry.Subtitle,
                entry.Level,
                entry.AccentColor);
        }

        for (int i = 0; i < catalog.Npcs.Count; i++)
        {
            CvgArchetypeCatalogDto entry = catalog.Npcs[i];
            lib._npcs[entry.ArchetypeId] = CompileRig(
                Path.Combine(artRoot, entry.File),
                entry.Title,
                entry.Subtitle,
                1,
                entry.AccentColor);
        }

        for (int i = 0; i < catalog.Items.Count; i++)
        {
            CvgItemCatalogDto entry = catalog.Items[i];
            lib._items[entry.ItemId] = CompileItem(Path.Combine(artRoot, entry.File));
        }

        return lib;
    }

    /// <inheritdoc/>
    public bool TryGetCharacter(byte preset, [NotNullWhen(true)] out CompiledVectorRig? rig)
    {
        if (_characters.TryGetValue(preset, out rig))
        {
            return true;
        }

        return _characters.TryGetValue(0, out rig);
    }

    /// <inheritdoc/>
    public bool TryGetMonster(ushort archetypeId, [NotNullWhen(true)] out CompiledVectorRig? rig)
    {
        if (_monsters.TryGetValue(archetypeId, out rig))
        {
            return true;
        }

        return _monsters.TryGetValue(101, out rig);
    }

    /// <inheritdoc/>
    public bool TryGetNpc(ushort archetypeId, [NotNullWhen(true)] out CompiledVectorRig? rig)
    {
        if (_npcs.TryGetValue(archetypeId, out rig))
        {
            return true;
        }

        return _npcs.TryGetValue(201, out rig);
    }

    /// <inheritdoc/>
    public bool TryGetItem(uint itemId, [NotNullWhen(true)] out CompiledItemVectorArt? item) =>
        _items.TryGetValue(itemId, out item);

    private static CompiledVectorRig CompileRig(
        string filePath,
        string title,
        string subtitle,
        byte level,
        string fallbackAccentHex)
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
            shapes,
            anims);
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
            shapes);
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
                pts);
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
            dict[kvp.Key] = new CompiledVectorAnimation(src.Duration, src.Loop, frames);
        }

        return dict;
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
