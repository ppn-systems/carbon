// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Primitives.Hashing;
using Carbon.Primitives.Math;

namespace Carbon.Maps.Landmarks;

/// <summary>Lays out the structures of one kind of point of interest.</summary>
public interface ILandmarkPlanner
{
    /// <summary>Adds the structures and fields of the point of interest to the context.</summary>
    /// <param name="context">The point of interest, terrain access and the layout being built.</param>
    void Plan(LandmarkPlanContext context);
}

/// <summary>
/// The planner of each kind of point of interest. To add one, write an <see cref="ILandmarkPlanner"/>,
/// register it here and add its site rule to <see cref="LandmarkRules"/>.
/// </summary>
public static class LandmarkPlanners
{
    private static readonly Dictionary<PointOfInterestType, ILandmarkPlanner> s_planners = new()
    {
        [PointOfInterestType.RiverVillage] = new VillagePlanner(LandmarkPiece.StyleTemperate, withFields: true),
        [PointOfInterestType.DesertVillage] = new VillagePlanner(LandmarkPiece.StyleDesert, withFields: false),
        [PointOfInterestType.SnowVillage] = new VillagePlanner(LandmarkPiece.StyleSnow, withFields: false),
        [PointOfInterestType.SwampVillage] = new VillagePlanner(LandmarkPiece.StyleSwamp, withFields: false),
        [PointOfInterestType.Harbor] = new HarborPlanner(),
        [PointOfInterestType.LakesideCamp] = new CampPlanner(),
        [PointOfInterestType.WorldTree] = new WorldTreePlanner(),
        [PointOfInterestType.MountainShrine] = new ShrinePlanner(),
        [PointOfInterestType.MineEntrance] = new MinePlanner(),
    };

    /// <summary>Gets the planner of a kind of point of interest, or <c>null</c>.</summary>
    /// <param name="type">The kind.</param>
    /// <returns>The planner.</returns>
    public static ILandmarkPlanner? Get(PointOfInterestType type) => s_planners.GetValueOrDefault(type);
}

/// <summary>A point of interest being laid out: its centre, terrain access and the pieces placed so far.</summary>
public sealed class LandmarkPlanContext
{
    /// <summary>One pixel in tile units (pieces are sized in sprite pixels).</summary>
    public const float Px = 1f / 32f;

    private readonly ITileSource _tiles;
    private readonly List<LandmarkPiece> _pieces = new();
    private readonly List<LandmarkField> _fields = new();

    internal LandmarkPlanContext(in PointOfInterest poi, ITileSource tiles)
    {
        _tiles = tiles;
        this.Poi = poi;
        this.CenterX = poi.TileX + 0.5f;
        this.CenterY = poi.TileY + 0.5f;
        this.Radius = poi.Radius;
        this.Level = tiles.SampleTile(poi.TileX, poi.TileY).HeightLevel;
        this.Seed = SpatialHash.Hash(0x1A2B3C4D5E6F7081UL, poi.TileX, poi.TileY);
    }

    /// <summary>Gets the point of interest.</summary>
    public PointOfInterest Poi { get; }

    /// <summary>Gets the centre X in tiles.</summary>
    public float CenterX { get; }

    /// <summary>Gets the centre Y in tiles.</summary>
    public float CenterY { get; }

    /// <summary>Gets the footprint radius in tiles.</summary>
    public float Radius { get; }

    /// <summary>Gets the height level of the site.</summary>
    public byte Level { get; }

    /// <summary>Gets the layout seed.</summary>
    public ulong Seed { get; }

    /// <summary>Gets the pieces placed so far.</summary>
    public IReadOnlyList<LandmarkPiece> Pieces => _pieces;

    /// <summary>Adds a structure.</summary>
    public void Add(LandmarkPiece piece) => _pieces.Add(piece);

    /// <summary>Adds a crop field.</summary>
    public void Add(LandmarkField field) => _fields.Add(field);

    /// <summary>Deterministic 32-bit hash of the layout seed and an index.</summary>
    public uint Hash(int i) => SpatialHash.Hash32(this.Seed, i, 0, 0x4C41594FUL);

    /// <summary>Deterministic value in [0, 1] from the layout seed and an index.</summary>
    public float Hash01(int i) => (this.Hash(i) & 0xFFFF) / 65535f;

    /// <summary>Returns whether a footprint stands on dry ground at the site's level (no rivers, no cliff edges).</summary>
    public bool IsBuildable(float x, float y, float width)
    {
        for (int i = -2; i <= 2; i++)
        {
            for (int j = 0; j <= 3; j++)
            {
                int tx = (int)MathF.Floor(x + (i * width * 0.25f));
                int ty = (int)MathF.Floor(y - (j * LandmarkPiece.HouseDepthTiles / 3f));
                MapTile t = _tiles.SampleTile(tx, ty);
                if (t.IsSolid || t.HeightLevel != this.Level || t.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Returns whether a spot lies within <paramref name="clearance"/> of a placed piece.</summary>
    public bool Overlaps(float x, float y, float clearance)
    {
        foreach (LandmarkPiece piece in _pieces)
        {
            float dx = piece.X - x;
            float dy = piece.Y - y;
            if ((dx * dx) + (dy * dy) < clearance * clearance)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Deterministic sine (identical on every platform).</summary>
    public static float Sin(float a) => DeterministicMath.Sin(a);

    /// <summary>Deterministic cosine (identical on every platform).</summary>
    public static float Cos(float a) => DeterministicMath.Cos(a);

    internal LandmarkLayout ToLayout() => new([.. _pieces], [.. _fields]);
}

/// <summary>A well, a ring of houses, optional crop fields and a barrel.</summary>
internal sealed class VillagePlanner(byte style, bool withFields) : ILandmarkPlanner
{
    public void Plan(LandmarkPlanContext c)
    {
        const float Px = LandmarkPlanContext.Px;
        float cx = c.CenterX;
        float cy = c.CenterY;
        float r = c.Radius;
        c.Add(new LandmarkPiece(LandmarkPieceKind.Well, cx, cy + (10f * Px), 1f, 0));
        int houses = 0;
        for (int i = 0; i < 9 && houses < 7; i++)
        {
            float a = (i * (MathF.Tau / 9f)) + (c.Hash01(i) * 0.35f);
            float dist = r * (0.58f + (c.Hash01(i + 40) * 0.18f));
            float x = cx + (LandmarkPlanContext.Cos(a) * dist);
            float y = cy + (LandmarkPlanContext.Sin(a) * dist * 0.85f);
            float w = (144f + (c.Hash01(i + 80) * 32f)) * Px;
            if (c.IsBuildable(x, y, w) && !c.Overlaps(x, y, w + (40f * Px)))
            {
                c.Add(new LandmarkPiece(LandmarkPieceKind.House, x, y, w, (byte)((c.Hash(i + 120) % 6) | (uint)(style << 4))));
                houses++;
            }
        }

        for (int i = 0; i < (withFields ? 6 : 0); i++)
        {
            float a = (i * (MathF.Tau / 6f)) + 0.5f + (c.Hash01(i + 200) * 0.3f);
            float fx = cx + (LandmarkPlanContext.Cos(a) * r * 0.86f);
            float fy = cy + (LandmarkPlanContext.Sin(a) * r * 0.86f);
            float hx = (36f + (c.Hash01(i + 210) * 18f)) * Px;
            float hy = (22f + (c.Hash01(i + 220) * 12f)) * Px;
            if (c.IsBuildable(fx, fy, hx * 2f) && !c.Overlaps(fx, fy, hx + (40f * Px)))
            {
                c.Add(new LandmarkField(fx - hx, fy - hy, fx + hx, fy + hy, (byte)(c.Hash(i + 230) % 3)));
            }
        }

        c.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, cx + (42f * Px), cy + (34f * Px), 1f, 0));
    }
}

/// <summary>A warehouse facing the sea, flanking houses and barrels on the quay.</summary>
internal sealed class HarborPlanner : ILandmarkPlanner
{
    public void Plan(LandmarkPlanContext c)
    {
        const float Px = LandmarkPlanContext.Px;
        PointOfInterest poi = c.Poi;
        float len = MathF.Sqrt((poi.FacingX * poi.FacingX) + (poi.FacingY * poi.FacingY));
        (float sx, float sy) = len > 0.1f ? (poi.FacingX / len, poi.FacingY / len) : (0f, 1f);
        float px = -sy;
        float py = sx;
        float cx = c.CenterX;
        float cy = c.CenterY;
        float ix = cx - (sx * c.Radius * 0.30f);
        float iy = cy - (sy * c.Radius * 0.30f);
        c.Add(new LandmarkPiece(LandmarkPieceKind.Warehouse, ix, iy, 192f * Px, (byte)(c.Hash(1) % 6)));
        for (int i = -1; i <= 1; i += 2)
        {
            float x = ix + (px * i * 220f * Px) + (sx * 20f * Px);
            float y = iy + (py * i * 220f * Px) + (sy * 20f * Px);
            if (c.IsBuildable(x, y, 144f * Px))
            {
                c.Add(new LandmarkPiece(LandmarkPieceKind.House, x, y, 144f * Px, (byte)(c.Hash(i + 5) % 6)));
            }
        }

        c.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, cx + (px * 46f * Px) + (sx * 30f * Px), cy + (py * 46f * Px) + (sy * 30f * Px), 1f, 0));
        c.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, cx - (px * 52f * Px) + (sx * 36f * Px), cy - (py * 52f * Px) + (sy * 36f * Px), 1f, 1));
    }
}

/// <summary>A campfire with tents around it.</summary>
internal sealed class CampPlanner : ILandmarkPlanner
{
    public void Plan(LandmarkPlanContext c)
    {
        const float Px = LandmarkPlanContext.Px;
        float cx = c.CenterX;
        float cy = c.CenterY;
        c.Add(new LandmarkPiece(LandmarkPieceKind.Campfire, cx, cy, 1f, 0));
        for (int i = 0; i < 3; i++)
        {
            float a = (-MathF.PI / 2f) + ((i - 1) * 1.1f) + (c.Hash01(i) * 0.3f);
            float x = cx + (LandmarkPlanContext.Cos(a) * 76f * Px);
            float y = cy + (LandmarkPlanContext.Sin(a) * 62f * Px);
            if (c.IsBuildable(x, y, 56f * Px))
            {
                c.Add(new LandmarkPiece(LandmarkPieceKind.Tent, x, y, (56f + (c.Hash01(i + 9) * 10f)) * Px, (byte)(c.Hash(i + 20) % 3)));
            }
        }
    }
}

/// <summary>The World Tree ringed by stone lanterns.</summary>
internal sealed class WorldTreePlanner : ILandmarkPlanner
{
    public void Plan(LandmarkPlanContext c)
    {
        float cx = c.CenterX;
        float cy = c.CenterY;
        c.Add(new LandmarkPiece(LandmarkPieceKind.WorldTree, cx, cy + 1f, 96f * LandmarkPlanContext.Px, (byte)(c.Hash(3) % 4)));
        for (int i = 0; i < 6; i++)
        {
            float a = (i * (MathF.Tau / 6f)) + 0.26f;
            c.Add(new LandmarkPiece(LandmarkPieceKind.StoneLantern, cx + (LandmarkPlanContext.Cos(a) * 8.5f), cy + 1f + (LandmarkPlanContext.Sin(a) * 7f), 1f, 1));
        }
    }
}

/// <summary>An altar behind a shrine gate, flanked by lanterns.</summary>
internal sealed class ShrinePlanner : ILandmarkPlanner
{
    public void Plan(LandmarkPlanContext c)
    {
        const float Px = LandmarkPlanContext.Px;
        float cx = c.CenterX;
        float cy = c.CenterY;
        c.Add(new LandmarkPiece(LandmarkPieceKind.Altar, cx, cy - (24f * Px), 1f, 0));
        c.Add(new LandmarkPiece(LandmarkPieceKind.ShrineGate, cx, cy + (c.Radius * 0.62f), 92f * Px, 0));
        c.Add(new LandmarkPiece(LandmarkPieceKind.StoneLantern, cx - (58f * Px), cy + (18f * Px), 1f, 0));
        c.Add(new LandmarkPiece(LandmarkPieceKind.StoneLantern, cx + (58f * Px), cy + (18f * Px), 1f, 0));
    }
}

/// <summary>A mine portal with an ore cart, a lantern and spoil heaps of rock.</summary>
internal sealed class MinePlanner : ILandmarkPlanner
{
    public void Plan(LandmarkPlanContext c)
    {
        const float Px = LandmarkPlanContext.Px;
        float cx = c.CenterX;
        float cy = c.CenterY;
        c.Add(new LandmarkPiece(LandmarkPieceKind.MineEntrance, cx, cy - (8f * Px), 112f * Px, (byte)(c.Hash(1) % 4)));
        float side = c.Hash01(2) < 0.5f ? -1f : 1f;
        float cartX = cx + (side * 78f * Px);
        float cartY = cy + (30f * Px);
        if (c.IsBuildable(cartX, cartY, 40f * Px))
        {
            c.Add(new LandmarkPiece(LandmarkPieceKind.OreCart, cartX, cartY, 1f, (byte)(c.Hash(3) % 3)));
        }

        float lampX = cx - (side * 70f * Px);
        float lampY = cy + (20f * Px);
        if (c.IsBuildable(lampX, lampY, 20f * Px))
        {
            c.Add(new LandmarkPiece(LandmarkPieceKind.StoneLantern, lampX, lampY, 1f, 0));
        }
    }
}
