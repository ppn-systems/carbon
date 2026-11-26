// Copyright (c) 2026 PPN Systems. All rights reserved.

using System.Numerics;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Pre-compiled declarative vector artwork and grip metadata for a starter item, tool, weapon, or structure kit.
/// </summary>
public sealed class CompiledItemVectorArt
{
    /// <summary>Initializes a compiled item vector blueprint.</summary>
    public CompiledItemVectorArt(
        uint itemId,
        string key,
        string displayName,
        byte tier,
        string categoryLabel,
        ColorRgba accentColor,
        Vector2 gripPoint,
        float restAngleRad,
        float worldScale,
        CompiledVectorShape[] shapes,
        bool isHeldVisible = true,
        ColorRgba armorPrimaryColor = default,
        ColorRgba armorSecondaryColor = default,
        ColorRgba armorTrimColor = default)
    {
        this.ItemId = itemId;
        this.Key = key;
        this.DisplayName = displayName;
        this.Tier = tier;
        this.CategoryLabel = categoryLabel;
        this.AccentColor = accentColor;
        this.GripPoint = gripPoint;
        this.RestAngleRad = restAngleRad;
        this.WorldScale = worldScale;
        this.Shapes = shapes;
        this.IsHeldVisible = isHeldVisible;
        this.ArmorPrimaryColor = armorPrimaryColor;
        this.ArmorSecondaryColor = armorSecondaryColor;
        this.ArmorTrimColor = armorTrimColor;
    }

    /// <summary>Whether holding this item on the active hotbar renders it in the hero's hand at rest.</summary>
    public bool IsHeldVisible { get; }

    /// <summary>Primary color applied to worn armor overlay composites (when item is equipment).</summary>
    public ColorRgba ArmorPrimaryColor { get; }

    /// <summary>Secondary shade color applied to worn armor overlay composites.</summary>
    public ColorRgba ArmorSecondaryColor { get; }

    /// <summary>Trim/highlight accent color applied to worn armor overlay composites.</summary>
    public ColorRgba ArmorTrimColor { get; }

    /// <summary>Domain item ID (<c>1..12</c>).</summary>
    public uint ItemId { get; }

    /// <summary>Canonical item key.</summary>
    public string Key { get; }

    /// <summary>Display name.</summary>
    public string DisplayName { get; }

    /// <summary>Tool/rarity tier (<c>1..3</c>).</summary>
    public byte Tier { get; }

    /// <summary>Category tag shown in RPG tooltips.</summary>
    public string CategoryLabel { get; }

    /// <summary>Tier/rarity accent color.</summary>
    public ColorRgba AccentColor { get; }

    /// <summary>Grip pivot in local <c>[-10..10]</c> design space.</summary>
    public Vector2 GripPoint { get; }

    /// <summary>Resting idle tilt angle in radians.</summary>
    public float RestAngleRad { get; }

    /// <summary>Held world draw scale.</summary>
    public float WorldScale { get; }

    /// <summary>Compiled vector primitives.</summary>
    public CompiledVectorShape[] Shapes { get; }
}
