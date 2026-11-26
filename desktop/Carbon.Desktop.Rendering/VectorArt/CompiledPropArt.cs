// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Pre-compiled point light configuration emitted by luminescent world props such as crystals and shrines.
/// </summary>
/// <param name="Offset">Local offset from prop world origin.</param>
/// <param name="Radius">Attenuation radius in world units.</param>
/// <param name="Color">Additive glow color.</param>
/// <param name="Intensity">Glow brightness factor.</param>
/// <param name="RareOnly">Whether the light only activates when the rare final palette variant is chosen.</param>
public readonly record struct CompiledPropLight(
    Vector2 Offset,
    float Radius,
    ColorRgba Color,
    float Intensity,
    bool RareOnly);

/// <summary>
/// Pre-compiled declarative vector artwork for a natural world prop (<c>props/*.cvg.json</c>),
/// with all palette variants pre-baked at load time for zero-allocation rendering.
/// </summary>
public sealed class CompiledPropArt
{
    /// <summary>Initializes a compiled prop vector blueprint.</summary>
    public CompiledPropArt(
        byte propType,
        string id,
        string displayName,
        float designRadius,
        bool hasCanopy,
        bool allowMirror,
        bool isWoody,
        bool isIcy,
        float rareLastVariantChance,
        CompiledPropLight? light,
        CompiledVectorShape[][] groundVariants,
        CompiledVectorShape[][] canopyVariants,
        CompiledVectorShape[][] remnantVariants)
    {
        this.PropType = propType;
        this.Id = id;
        this.DisplayName = displayName;
        this.DesignRadius = MathF.Max(1f, designRadius);
        this.HasCanopy = hasCanopy;
        this.AllowMirror = allowMirror;
        this.IsWoody = isWoody;
        this.IsIcy = isIcy;
        this.RareLastVariantChance = Math.Clamp(rareLastVariantChance, 0f, 1f);
        this.Light = light;
        this.GroundVariants = groundVariants;
        this.CanopyVariants = canopyVariants;
        this.RemnantVariants = remnantVariants;
    }

    /// <summary>Numeric prop type matching <c>MapPropType</c>.</summary>
    public byte PropType { get; }

    /// <summary>Canonical blueprint identifier.</summary>
    public string Id { get; }

    /// <summary>Human-readable display name.</summary>
    public string DisplayName { get; }

    /// <summary>Nominal design-space radius used to scale instances accurately to runtime radius.</summary>
    public float DesignRadius { get; }

    /// <summary>Whether the prop renders an overhead canopy layer above actors.</summary>
    public bool HasCanopy { get; }

    /// <summary>Whether instances may mirror horizontally from their coordinate hash.</summary>
    public bool AllowMirror { get; }

    /// <summary>Whether the prop displays timber axe-notch damage and stumps when harvested.</summary>
    public bool IsWoody { get; }

    /// <summary>Whether the prop renders icy crystal fracture highlights when mined.</summary>
    public bool IsIcy { get; }

    /// <summary>Probability <c>[0..1]</c> of selecting the final palette variant when <see cref="Light"/> marks <see cref="CompiledPropLight.RareOnly"/>.</summary>
    public float RareLastVariantChance { get; }

    /// <summary>Optional nocturnal additive light source.</summary>
    public CompiledPropLight? Light { get; }

    /// <summary>Pre-baked ground/trunk primitive arrays indexed by palette variant.</summary>
    public CompiledVectorShape[][] GroundVariants { get; }

    /// <summary>Pre-baked canopy primitive arrays indexed by palette variant.</summary>
    public CompiledVectorShape[][] CanopyVariants { get; }

    /// <summary>Pre-baked destroyed remnant primitive arrays indexed by palette variant.</summary>
    public CompiledVectorShape[][] RemnantVariants { get; }

    /// <summary>Deterministically picks a palette variant index for an instance hash in <c>O(1)</c>.</summary>
    public int SelectVariantIndex(uint hash, out bool isRareVariant)
    {
        int count = this.GroundVariants.Length;
        if (count <= 1)
        {
            isRareVariant = count == 1 && this.Light is { RareOnly: true };
            return 0;
        }

        if (this.Light is { RareOnly: true } && this.RareLastVariantChance > 0f)
        {
            float roll = AmbientAnimator.Hash01(hash, 401);
            if (roll < this.RareLastVariantChance)
            {
                isRareVariant = true;
                return count - 1;
            }

            isRareVariant = false;
            int commonCount = count - 1;
            return (int)((hash >> 8) % (uint)commonCount);
        }

        isRareVariant = false;
        return (int)((hash >> 8) % (uint)count);
    }
}
