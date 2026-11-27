// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Carbon.Desktop.Rendering.VectorArt;

internal sealed class CvgCatalogDto
{
    public int SchemaVersion { get; set; } = 2;
    public List<CvgCharacterCatalogDto> Characters { get; set; } = [];
    public List<CvgArchetypeCatalogDto> Monsters { get; set; } = [];
    public List<CvgArchetypeCatalogDto> Npcs { get; set; } = [];
    public List<CvgItemCatalogDto> Items { get; set; } = [];
    public List<CvgPropCatalogDto> Props { get; set; } = [];
    public List<CvgBiomeCatalogDto> Biomes { get; set; } = [];
    public List<CvgStructureCatalogDto> Structures { get; set; } = [];
    public List<CvgUiIconCatalogDto> UiIcons { get; set; } = [];
    public CvgUiIconCatalogDto? TreeSapling { get; set; }
}

internal sealed class CvgUiIconCatalogDto
{
    public string Id { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
}

internal sealed class CvgPropCatalogDto
{
    public byte PropType { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
}

internal sealed class CvgPointLightDto
{
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public float Radius { get; set; } = 64f;
    public string Color { get; set; } = "#FFFFFF";
    public float Intensity { get; set; } = 0.7f;
    public bool RareOnly { get; set; }
}

internal sealed class CvgPropBlueprintDto
{
    public int SchemaVersion { get; set; } = 2;
    public byte PropType { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public float DesignRadius { get; set; } = 20f;
    public bool HasCanopy { get; set; }
    public bool AllowMirror { get; set; } = true;
    public bool IsWoody { get; set; }
    public bool IsIcy { get; set; }
    public float RareLastVariantChance { get; set; } = 0.12f;
    public CvgPointLightDto? Light { get; set; }
    public List<Dictionary<string, string>> PaletteVariants { get; set; } = [];
    public List<CvgShapeDto> GroundShapes { get; set; } = [];
    public List<CvgShapeDto> CanopyShapes { get; set; } = [];
    public List<CvgShapeDto> RemnantShapes { get; set; } = [];
}

internal sealed class CvgBiomeCatalogDto
{
    public byte BiomeId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
}

internal sealed class CvgBiomeDetailVariantDto
{
    public List<CvgShapeDto> Shapes { get; set; } = [];
}

internal sealed class CvgBiomeBlueprintDto
{
    public int SchemaVersion { get; set; } = 2;
    public byte BiomeId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string GroundColor { get; set; } = "#4A824B";
    public string MinimapColor { get; set; } = "#56964A";
    public string CliffBase { get; set; } = "#464C52";
    public string CliffStrata { get; set; } = "#363B41";
    public string CliffHighlight { get; set; } = "#A5ACB4E6";
    public string CliffShadow { get; set; } = "#24282D";
    public int DetailChancePercent { get; set; } = 16;
    public Dictionary<string, string> Palette { get; set; } = [];
    public List<CvgBiomeDetailVariantDto> DetailVariants { get; set; } = [];
    public CvgBiomeSurfaceDto? Surface { get; set; }
    public List<CvgBiomeDecalLayerDto> Decals { get; set; } = [];
}

internal sealed class CvgBiomeSurfaceDto
{
    public string Pattern { get; set; } = "patches";
    public List<string> Colors { get; set; } = [];
    public string? Accent { get; set; }
    public float Scale { get; set; } = 1f;
    public float Variation { get; set; } = 0.06f;
    public float Grain { get; set; } = 0.05f;
}

internal sealed class CvgBiomeDecalLayerDto
{
    public string Id { get; set; } = string.Empty;
    public float Chance { get; set; }
    public int MaxCount { get; set; } = 1;
    public string Placement { get; set; } = "any";
    public bool Jitter { get; set; } = true;
    public List<CvgBiomeDetailVariantDto> Variants { get; set; } = [];
}

internal sealed class CvgStructureCatalogDto
{
    public byte StructureType { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
}

internal sealed class CvgStructureBlueprintDto
{
    public int SchemaVersion { get; set; } = 2;
    public byte StructureType { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Dictionary<string, string> Palette { get; set; } = [];
    public List<CvgShapeDto> Shapes { get; set; } = [];
    public List<CvgShapeDto> OpenShapes { get; set; } = [];
    public List<int> Size { get; set; } = [];
}

internal sealed class CvgCharacterCatalogDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string AccentColor { get; set; } = "#38BDF8";
    public string File { get; set; } = string.Empty;
}

internal sealed class CvgArchetypeCatalogDto
{
    public ushort ArchetypeId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public byte Level { get; set; } = 1;
    public string AccentColor { get; set; } = "#F87171";
    public string File { get; set; } = string.Empty;
    public bool Elite { get; set; }
}

internal sealed class CvgItemCatalogDto
{
    public uint ItemId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public byte Tier { get; set; } = 1;
    public string AccentColor { get; set; } = "#94A3B8";
    public string File { get; set; } = string.Empty;
}

internal sealed class CvgPointDto
{
    public float X { get; set; }
    public float Y { get; set; }
}

internal sealed class CvgShapeDto
{
    public string Kind { get; set; } = "ellipse";
    public string Bone { get; set; } = "body";
    public string Facing { get; set; } = "any";
    public string? Role { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Rx { get; set; }
    public float Ry { get; set; }
    public float W { get; set; }
    public float H { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
    public float Thickness { get; set; } = 1.5f;
    public float RotDeg { get; set; }
    public List<float[]>? Points { get; set; }
    public string Color { get; set; } = "#FFFFFF";
    public string? OutlineColor { get; set; }
    public float OutlineThickness { get; set; } = 1.1f;
}

internal sealed class CvgBonePoseDto
{
    public float Dx { get; set; }
    public float Dy { get; set; }
    public float RotDeg { get; set; }
    public float? ScaleX { get; set; }
    public float? ScaleY { get; set; }
}

internal sealed class CvgKeyframeDto
{
    public float T { get; set; }
    public Dictionary<string, CvgBonePoseDto> Bones { get; set; } = [];
}

internal sealed class CvgAnimDto
{
    public float Duration { get; set; } = 1f;
    public bool Loop { get; set; } = true;
    public CvgPhaseMarksDto? PhaseMarks { get; set; }
    public List<CvgKeyframeDto> Tracks { get; set; } = [];
}

internal sealed class CvgPhaseMarksDto
{
    public float WindupEnd { get; set; } = AttackPhaseMarks.Default.WindupEnd;
    public float TravelEnd { get; set; } = AttackPhaseMarks.Default.TravelEnd;
    public float ActiveEnd { get; set; } = AttackPhaseMarks.Default.ActiveEnd;
}

internal sealed class CvgEntityRigDto
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public bool SupportsModularHuman { get; set; }
    public float DesignHeight { get; set; } = 34f;
    public float WorldHeight { get; set; } = 34f;
    public float HeadTopOffset { get; set; } = 30f;
    public string AccentColor { get; set; } = "#38BDF8";
    public Dictionary<string, CvgPointDto>? WeaponGrip { get; set; }
    public Dictionary<string, CvgPointDto>? BonePivots { get; set; }
    public Dictionary<string, string> Palette { get; set; } = [];
    public List<CvgShapeDto> Shapes { get; set; } = [];
    public Dictionary<string, CvgAnimDto> Animations { get; set; } = [];
    public CvgRigMarkersDto? Markers { get; set; }
}

internal sealed class CvgRigMarkersDto
{
    public List<CvgPointDto> Eyes { get; set; } = [];
    public List<CvgPointDto> EyesSide { get; set; } = [];
    public CvgPointDto? Crown { get; set; }
    public string EyeGlowColor { get; set; } = string.Empty;
}

internal sealed class CvgEquipmentVisualDto
{
    public string PrimaryColor { get; set; } = "#92400E";
    public string SecondaryColor { get; set; } = "#B45309";
    public string TrimColor { get; set; } = "#F59E0B";
}

internal sealed class CvgItemBlueprintDto
{
    public int SchemaVersion { get; set; } = 1;
    public uint ItemId { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public byte Tier { get; set; } = 1;
    public string CategoryLabel { get; set; } = "Item";
    public string AccentColor { get; set; } = "#94A3B8";
    public bool HeldVisible { get; set; } = true;
    public CvgEquipmentVisualDto? ArmorVisual { get; set; }
    public float GripX { get; set; }
    public float GripY { get; set; }
    public float RestAngleDeg { get; set; }
    public float WorldScale { get; set; } = 1f;
    public Dictionary<string, string> Palette { get; set; } = [];
    public List<CvgShapeDto> Shapes { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CvgCatalogDto))]
[JsonSerializable(typeof(CvgEntityRigDto))]
[JsonSerializable(typeof(CvgItemBlueprintDto))]
[JsonSerializable(typeof(CvgPropBlueprintDto))]
[JsonSerializable(typeof(CvgBiomeBlueprintDto))]
[JsonSerializable(typeof(CvgStructureBlueprintDto))]
internal sealed partial class CvgJsonContext : JsonSerializerContext;
