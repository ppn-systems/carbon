// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>How a biome's bare-ground colour varies across the world (the <c>surface.pattern</c> of a biome blueprint).</summary>
public enum BiomeSurfacePattern : byte
{
    /// <summary>Soft low-frequency patches between the colour stops (meadows, forest floor).</summary>
    Patches = 0,

    /// <summary>Asymmetric dune ridges: long lit windward slopes and short shaded lee faces (stops: lee, mid, crest).</summary>
    Dunes = 1,

    /// <summary>Sedimentary bands along warped contours, cycling through every colour stop.</summary>
    Strata = 2,

    /// <summary>Wind-stretched snow drifts between the stops (hollow to crest).</summary>
    Drifts = 3,

    /// <summary>Patches with dark accent pools (peat bogs).</summary>
    Peat = 4,

    /// <summary>Patches broken by accent-coloured bare rock.</summary>
    Rock = 5,

    /// <summary>Water: gentle mid-frequency shimmer between the stops.</summary>
    Water = 6,
}

/// <summary>Where a decal layer may be stamped.</summary>
public enum BiomeDecalPlacement : byte
{
    /// <summary>On any tile of the biome.</summary>
    Any = 0,

    /// <summary>Only on tiles with no water around them.</summary>
    Dry = 1,

    /// <summary>Only on tiles touching water (banks, beaches, shallows by the shore).</summary>
    Shore = 2,
}

/// <summary>
/// Compiled ground colour field of a biome. Colours are 0..255 vectors so they can be blended before they are
/// quantised into vertex colours.
/// </summary>
/// <param name="Pattern">The field pattern.</param>
/// <param name="Colors">Colour stops (at least one).</param>
/// <param name="Accent">Accent colour used by the peat and rock patterns.</param>
/// <param name="Scale">Spatial scale multiplier (larger = bigger features).</param>
/// <param name="Variation">Mid-frequency brightness variation (fraction).</param>
/// <param name="Grain">Per-vertex brightness jitter (fraction).</param>
public sealed record BiomeSurfaceStyle(
    BiomeSurfacePattern Pattern,
    Vector3[] Colors,
    Vector3 Accent,
    float Scale,
    float Variation,
    float Grain)
{
    /// <summary>Creates a flat style from a single ground colour (blueprints without a <c>surface</c> block).</summary>
    /// <param name="ground">Ground colour as a 0..255 vector.</param>
    /// <returns>A patches style with two close stops around <paramref name="ground"/>.</returns>
    public static BiomeSurfaceStyle FromGround(Vector3 ground) =>
        new(BiomeSurfacePattern.Patches, [ground * 0.94f, ground * 1.04f], ground * 0.8f, 1f, 0.04f, 0.04f);
}

/// <summary>One decal drawing with its bounds in 32x32 tile space (used to jitter it without leaving the tile).</summary>
/// <param name="Shapes">The shapes.</param>
/// <param name="Min">Top-left of the bounds.</param>
/// <param name="Max">Bottom-right of the bounds.</param>
public readonly record struct BiomeDecalVariant(CompiledVectorShape[] Shapes, Vector2 Min, Vector2 Max);

/// <summary>A layer of small ground decals (tufts, pebbles, ripples ...) scattered over a biome's tiles.</summary>
/// <param name="Id">Layer name, for diagnostics.</param>
/// <param name="Chance">Chance (0..1) of each of up to <paramref name="MaxCount"/> stamps per tile.</param>
/// <param name="MaxCount">Maximum stamps per tile.</param>
/// <param name="Placement">Where the layer may be stamped.</param>
/// <param name="Jitter">Whether stamps move randomly inside the tile (else they stay where they were drawn).</param>
/// <param name="Variants">The drawings to pick from.</param>
public sealed record BiomeDecalLayer(
    string Id,
    float Chance,
    int MaxCount,
    BiomeDecalPlacement Placement,
    bool Jitter,
    BiomeDecalVariant[] Variants)
{
    /// <summary>Gets an empty layer list.</summary>
    public static BiomeDecalLayer[] None { get; } = [];

    /// <summary>Computes the 32x32 tile-space bounds of a set of shapes (outlines and stroke widths included).</summary>
    /// <param name="shapes">The shapes.</param>
    /// <returns>The variant with its bounds.</returns>
    public static BiomeDecalVariant Measure(CompiledVectorShape[] shapes)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        Vector2 min = new(float.MaxValue);
        Vector2 max = new(float.MinValue);
        foreach (CompiledVectorShape s in shapes)
        {
            float pad = s.HasOutline ? s.OutlineThickness : 0f;
            switch (s.Kind)
            {
                case VectorShapeKind.Rect:
                    {
                        // Rotated rectangles: bound by the circle through their corners.
                        Vector2 centre = s.P0 + (s.P1 * 0.5f);
                        Vector2 half = MathF.Abs(s.RotRad) < 0.001f ? s.P1 * 0.5f : new Vector2(s.P1.Length() * 0.5f);
                        Grow(ref min, ref max, centre - half - new Vector2(pad), centre + half + new Vector2(pad));
                        break;
                    }

                case VectorShapeKind.Polygon when s.PolygonPoints is { } pts:
                    foreach (Vector2 p in pts)
                    {
                        Grow(ref min, ref max, p - new Vector2(pad), p + new Vector2(pad));
                    }

                    break;

                case VectorShapeKind.Capsule:
                case VectorShapeKind.Line:
                    {
                        Vector2 r = new((s.Thickness * 0.5f) + pad);
                        Grow(ref min, ref max, Vector2.Min(s.P0, s.P1) - r, Vector2.Max(s.P0, s.P1) + r);
                        break;
                    }

                case VectorShapeKind.Ring:
                    {
                        Vector2 r = new(s.P1.X + s.Thickness);
                        Grow(ref min, ref max, s.P0 - r, s.P0 + r);
                        break;
                    }

                case VectorShapeKind.Ellipse:
                case VectorShapeKind.Polygon:
                default:
                    {
                        Vector2 r = new(MathF.Max(s.P1.X, s.P1.Y) + pad);
                        Grow(ref min, ref max, s.P0 - r, s.P0 + r);
                        break;
                    }
            }
        }

        return shapes.Length == 0 ? new BiomeDecalVariant(shapes, Vector2.Zero, Vector2.Zero) : new BiomeDecalVariant(shapes, min, max);
    }

    private static void Grow(ref Vector2 min, ref Vector2 max, Vector2 lo, Vector2 hi)
    {
        min = Vector2.Min(min, lo);
        max = Vector2.Max(max, hi);
    }
}
