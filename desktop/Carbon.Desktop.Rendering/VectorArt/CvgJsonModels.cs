// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Carbon.Desktop.Rendering.VectorArt;

internal sealed class CvgCatalogDto
{
    public int SchemaVersion { get; set; } = 1;
    public List<CvgCharacterCatalogDto> Characters { get; set; } = [];
    public List<CvgArchetypeCatalogDto> Monsters { get; set; } = [];
    public List<CvgArchetypeCatalogDto> Npcs { get; set; } = [];
    public List<CvgItemCatalogDto> Items { get; set; } = [];
}

internal sealed class CvgCharacterCatalogDto
{
    public byte Preset { get; set; }
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
    public List<CvgKeyframeDto> Tracks { get; set; } = [];
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
internal sealed partial class CvgJsonContext : JsonSerializerContext;
