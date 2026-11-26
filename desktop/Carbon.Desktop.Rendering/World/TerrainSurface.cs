// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Procedural;
using Carbon.Maps.Biomes;
using Carbon.Maps.Noise;

namespace Carbon.Desktop.Rendering.World;

/// <summary>
/// The look of bare ground, baked once into each chunk mesh: a continuous colour field per biome (meadow
/// patches, dune ridges, mesa strata, snow drifts ...) described by the biome blueprint's <c>surface</c> block,
/// and the blueprint's scattered ground decals. Everything is a pure function of world coordinates, so tiles
/// agree across chunk seams and a chunk always rebakes identically.
/// </summary>
internal static class TerrainSurface
{
    private static readonly SimplexNoise2D s_low = new(0x7E44A1C35B2D9F01UL);
    private static readonly SimplexNoise2D s_mid = new(0x19D3B66FA0C4E827UL);
    private static readonly SimplexNoise2D s_drift = new(0x5C0FFEE1D2A3B4C5UL);

    /// <summary>The noise values a ground colour is made from, sampled once per mesh vertex position.</summary>
    /// <param name="X">World X in tile units.</param>
    /// <param name="Y">World Y in tile units.</param>
    /// <param name="Low">Low-frequency noise (patches, ~22 tiles), -1..1.</param>
    /// <param name="Mid">Mid-frequency noise (~6 tiles), -1..1.</param>
    /// <param name="Drift">Anisotropic noise stretched along X (snow drifts), -1..1.</param>
    /// <param name="Grain">Per-point hash, 0..1.</param>
    public readonly record struct Field(float X, float Y, float Low, float Mid, float Drift, float Grain);

    /// <summary>Gets a value indicating whether the biome is open water.</summary>
    /// <param name="biome">The biome.</param>
    /// <returns><c>true</c> for shallow and deep water.</returns>
    public static bool IsWater(BiomeType biome) => biome is BiomeType.ShallowWater or BiomeType.DeepOcean;

    /// <summary>Samples the noise field at a point given in world tile units.</summary>
    /// <param name="x">World X in tiles.</param>
    /// <param name="y">World Y in tiles.</param>
    /// <returns>The field sample.</returns>
    public static Field Sample(float x, float y) => new(
        x,
        y,
        s_low.Evaluate(x * 0.045f, y * 0.045f),
        s_mid.Evaluate(x * 0.16f, y * 0.16f),
        s_drift.Evaluate(x * 0.07f, y * 0.22f),
        Hash01(Hash((int)MathF.Round(x * 2f), (int)MathF.Round(y * 2f), 0x51u)));

    /// <summary>
    /// Ground colour (0..255 per channel) of a biome surface at a field sample, lifted for the tile's height
    /// level. Neighbouring tiles evaluate the same sample at a shared corner, so the field is seamless.
    /// </summary>
    /// <param name="style">The biome surface style.</param>
    /// <param name="heightLevel">The tile height level.</param>
    /// <param name="f">The field sample.</param>
    /// <returns>The colour.</returns>
    public static Vector3 Ground(BiomeSurfaceStyle style, int heightLevel, in Field f)
    {
        Vector3[] stops = style.Colors;
        float t = Smooth(0.5f + (0.5f * f.Low));
        Vector3 c;
        switch (style.Pattern)
        {
            case BiomeSurfacePattern.Dunes:
                {
                    // Asymmetric dune ridges: a long sunlit windward slope, then a short shaded lee face.
                    float phase = (((f.Y * 0.14f) + (f.X * 0.06f)) / style.Scale) + (f.Low * 1.4f) + (f.Mid * 0.25f);
                    float k = phase - MathF.Floor(phase);
                    float lit = k < 0.72f ? k / 0.72f : 1f - ((k - 0.72f) / 0.28f);
                    c = Gradient(stops, Smooth(lit));
                    break;
                }

            case BiomeSurfacePattern.Strata:
                {
                    // Sedimentary strata: bands that follow a warped contour, softly blended at their edges.
                    float v = (((f.Y * 0.2f) + (f.X * 0.05f)) / style.Scale) + (f.Low * 2.6f) + (f.Mid * 0.35f);
                    int band = (int)MathF.Floor(v);
                    float k = v - band;
                    int i0 = ((band % stops.Length) + stops.Length) % stops.Length;
                    int i1 = (i0 + 1) % stops.Length;
                    c = Vector3.Lerp(stops[i0], stops[i1], Smooth(Math.Clamp((k - 0.7f) / 0.3f, 0f, 1f)));
                    break;
                }

            case BiomeSurfacePattern.Drifts:
                {
                    // Wind-stretched drifts: bright crests, blue-grey hollows.
                    float k = Smooth(Math.Clamp(0.5f + (0.45f * f.Drift) + (0.3f * f.Low), 0f, 1f));
                    c = Gradient(stops, k);
                    break;
                }

            case BiomeSurfacePattern.Water:
                c = Gradient(stops, Smooth(0.5f + (0.5f * f.Mid)));
                break;

            case BiomeSurfacePattern.Peat:
                c = Gradient(stops, t);
                c = Vector3.Lerp(c, style.Accent, Math.Clamp((f.Mid - 0.25f) * 1.6f, 0f, 1f));
                break;

            case BiomeSurfacePattern.Rock:
                c = Gradient(stops, t);
                c = Vector3.Lerp(c, style.Accent, Math.Clamp((f.Mid - 0.4f) * 2f, 0f, 0.85f));
                break;

            case BiomeSurfacePattern.Patches:
            default:
                c = Gradient(stops, t);
                break;
        }

        c *= 1f + (f.Mid * style.Variation) + ((f.Grain - 0.5f) * style.Grain);
        float lift = heightLevel * 14f;
        float liftB = heightLevel == 2 ? 22f : heightLevel * 10f;
        return c + new Vector3(lift, lift, liftB);
    }

    /// <summary>Converts a 0..255 colour vector to a vertex colour.</summary>
    /// <param name="c">The colour.</param>
    /// <param name="alpha">The alpha.</param>
    /// <returns>The vertex colour.</returns>
    public static ColorRgba ToColor(Vector3 c, byte alpha = 255) =>
        new((byte)Math.Clamp(c.X, 0f, 255f), (byte)Math.Clamp(c.Y, 0f, 255f), (byte)Math.Clamp(c.Z, 0f, 255f), alpha);

    /// <summary>
    /// Stamps the biome's decal layers on one tile. Stamps are jittered only within the tile (later tiles
    /// would cover what spills east or south, and what spills north or west would cover cliff faces).
    /// </summary>
    /// <param name="b">The chunk mesh builder.</param>
    /// <param name="art">The biome blueprint.</param>
    /// <param name="wtx">World tile X.</param>
    /// <param name="wty">World tile Y.</param>
    /// <param name="x">Tile left in world units.</param>
    /// <param name="y">Tile top in world units.</param>
    /// <param name="s">Tile size in world units.</param>
    /// <param name="nearWater">Whether a water tile touches this tile.</param>
    public static void BakeDecals(ChunkMeshBuilder b, CompiledBiomeArt art, int wtx, int wty, float x, float y, float s, bool nearWater)
    {
        BiomeDecalLayer[] layers = art.DecalLayers;
        float unit = s / 32f;
        for (int l = 0; l < layers.Length; l++)
        {
            BiomeDecalLayer layer = layers[l];
            if ((layer.Placement == BiomeDecalPlacement.Dry && nearWater) ||
                (layer.Placement == BiomeDecalPlacement.Shore && !nearWater))
            {
                continue;
            }

            uint h = Hash(wtx, wty, 0xD3CA1u + ((uint)l * 0x9E3779B9u));
            for (int i = 0; i < layer.MaxCount; i++)
            {
                if (Roll(ref h) >= layer.Chance)
                {
                    continue;
                }

                BiomeDecalVariant v = layer.Variants[(int)(Next(ref h) % (uint)layer.Variants.Length)];
                bool mirror = (Next(ref h) & 1u) != 0;
                float dx = 0f;
                float dy = 0f;
                if (layer.Jitter)
                {
                    float minX = mirror ? 32f - v.Max.X : v.Min.X;
                    float maxX = mirror ? 32f - v.Min.X : v.Max.X;
                    dx = Spread(ref h, 1f - minX, 31f - maxX);
                    dy = Spread(ref h, 1f - v.Min.Y, 31f - v.Max.Y);
                }

                VectorMeshBaker.StampTileShapes(b, v.Shapes, x + (dx * unit), y + (dy * unit), s, mirror);
            }
        }
    }

    /// <summary>Hashes integer world coordinates with a salt.</summary>
    /// <param name="x">X.</param>
    /// <param name="y">Y.</param>
    /// <param name="salt">Salt.</param>
    /// <returns>The hash.</returns>
    public static uint Hash(int x, int y, uint salt)
    {
        uint h = ((uint)x * 0x8DA6B343u) ^ ((uint)y * 0xD8163841u) ^ (salt * 0xCB1AB31Fu);
        return Next(ref h);
    }

    // A random offset in [lo, hi]; when the decal is wider than the room it has, it stays centred.
    private static float Spread(ref uint h, float lo, float hi) =>
        hi <= lo ? (lo + hi) * 0.5f : lo + (Roll(ref h) * (hi - lo));

    private static Vector3 Gradient(Vector3[] stops, float t)
    {
        if (stops.Length == 1)
        {
            return stops[0];
        }

        float p = Math.Clamp(t, 0f, 1f) * (stops.Length - 1);
        int i = Math.Min((int)p, stops.Length - 2);
        return Vector3.Lerp(stops[i], stops[i + 1], p - i);
    }

    private static float Smooth(float t) => t * t * (3f - (2f * t));

    private static uint Next(ref uint h)
    {
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return h;
    }

    private static float Roll(ref uint h) => (Next(ref h) & 0xFFFFFF) / 16777216f;

    private static float Hash01(uint h) => (h & 0xFFFFFF) / 16777216f;
}
