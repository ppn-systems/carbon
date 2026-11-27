// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Settlements;
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
        [PointOfInterestType.RiverVillage] = new VillagePlanner(VillageStyle.Temperate, withFields: true),
        [PointOfInterestType.DesertVillage] = new VillagePlanner(VillageStyle.Desert, withFields: false),
        [PointOfInterestType.SnowVillage] = new VillagePlanner(VillageStyle.Snow, withFields: false),
        [PointOfInterestType.SwampVillage] = new VillagePlanner(VillageStyle.Swamp, withFields: false),
        [PointOfInterestType.Harbor] = new HarborPlanner(),
        [PointOfInterestType.LakesideCamp] = new CampPlanner(),
        [PointOfInterestType.WorldTree] = new WorldTreePlanner(),
        [PointOfInterestType.MountainShrine] = new ShrinePlanner(),
        [PointOfInterestType.MineEntrance] = new MinePlanner(),
        [PointOfInterestType.AncientRuins] = new AncientRuinsPlanner(),
        [PointOfInterestType.BanditCamp] = new BanditCampPlanner(),
        [PointOfInterestType.Watchtower] = new WatchtowerPlanner(),
        [PointOfInterestType.BossArena] = new BossArenaPlanner(),
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
    private readonly Func<int, int, bool>? _isRoad;
    private readonly Roads.RoadNetwork? _roads;
    private readonly List<LandmarkPiece> _pieces = new();
    private readonly List<LandmarkField> _fields = new();
    private readonly List<VillagePlot> _plots = new();
    private readonly List<LandmarkPath> _paths = new();
    private readonly List<(int X, int Y)> _paved = new();

    internal LandmarkPlanContext(in PointOfInterest poi, ITileSource tiles, StructureCatalog structures, Func<int, int, bool>? isRoad = null, Roads.RoadNetwork? roads = null)
    {
        _tiles = tiles;
        this.Structures = structures;
        _isRoad = isRoad;
        _roads = roads;
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

    /// <summary>Gets the building catalog (<c>structures.json</c>) whose footprints size village furniture.</summary>
    public StructureCatalog Structures { get; }

    /// <summary>Gets the pieces placed so far.</summary>
    public IReadOnlyList<LandmarkPiece> Pieces => _pieces;

    /// <summary>Gets the village building plots placed so far.</summary>
    public IReadOnlyList<VillagePlot> Plots => _plots;

    /// <summary>Adds a structure.</summary>
    public void Add(LandmarkPiece piece) => _pieces.Add(piece);

    /// <summary>Adds a village building plot (built from real building-grid structures, ADR-025).</summary>
    public void Add(VillagePlot plot) => _plots.Add(plot);

    /// <summary>Adds the centre line of a street or a door path.</summary>
    public void Add(LandmarkPath path) => _paths.Add(path);

    /// <summary>Adds a world tile the server paves with a floor structure.</summary>
    public void AddPaved(int tileX, int tileY) => _paved.Add((tileX, tileY));

    /// <summary>Returns whether a rectangle (tile units) comes within <paramref name="margin"/> of a dirt path's edge.</summary>
    public bool NearPath(float minX, float minY, float maxX, float maxY, float margin)
    {
        foreach (LandmarkPath p in _paths)
        {
            float reach = (p.Width * 0.5f) + margin;
            float length = MathF.Sqrt(((p.X1 - p.X0) * (p.X1 - p.X0)) + ((p.Y1 - p.Y0) * (p.Y1 - p.Y0)));
            int steps = Math.Max(1, (int)MathF.Ceiling(length * 4f));
            for (int k = 0; k <= steps; k++)
            {
                float t = k / (float)steps;
                float px = p.X0 + ((p.X1 - p.X0) * t);
                float py = p.Y0 + ((p.Y1 - p.Y0) * t);
                float ex = Math.Clamp(px, minX, maxX) - px;
                float ey = Math.Clamp(py, minY, maxY) - py;
                if ((ex * ex) + (ey * ey) <= reach * reach)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Samples the pure terrain at a world tile.</summary>
    public MapTile SampleTile(int tileX, int tileY) => _tiles.SampleTile(tileX, tileY);

    /// <summary>Returns whether a road or bridge runs over a world tile (always <c>false</c> without a road network).</summary>
    public bool IsRoad(int tileX, int tileY) => _isRoad?.Invoke(tileX, tileY) ?? false;

    /// <summary>
    /// Marks the road and bridge tiles of a rectangle (row-major, <paramref name="width"/> per row). Uses the road network
    /// in one pass when there is one, else <see cref="IsRoad"/> per tile.
    /// </summary>
    public void RasterizeRoads(int minX, int minY, int width, int height, Span<bool> output)
    {
        if (_roads is not null)
        {
            _roads.Rasterize(minX, minY, width, height, output);
            return;
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                output[(y * width) + x] = this.IsRoad(minX + x, minY + y);
            }
        }
    }

    /// <summary>Returns whether a world tile is dry, flat ground at the site's level.</summary>
    public bool IsBuildableTile(int tileX, int tileY)
    {
        MapTile t = _tiles.SampleTile(tileX, tileY);
        return !t.IsSolid && t.HeightLevel == this.Level && t.Biome is not (BiomeType.ShallowWater or BiomeType.DeepOcean);
    }

    /// <summary>
    /// Returns whether the tile rectangle, grown by <paramref name="margin"/> tiles, touches a placed village plot.
    /// </summary>
    public bool OverlapsPlot(int minX, int minY, int maxX, int maxY, int margin)
    {
        foreach (VillagePlot p in _plots)
        {
            if (minX - margin <= p.OriginX + p.Width - 1 && maxX + margin >= p.OriginX &&
                minY - margin <= p.OriginY + p.Height - 1 && maxY + margin >= p.OriginY)
            {
                return true;
            }
        }

        return false;
    }

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

    internal LandmarkLayout ToLayout() => new([.. _pieces], [.. _fields], [.. _plots], [.. _paths], [.. _paved]);
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
        float warehouseW = 192f * Px;
        float ix = cx - (sx * c.Radius * 0.30f);
        float iy = cy - (sy * c.Radius * 0.30f);

        // Step further inland if needed so the warehouse never straddles tidal water.
        if (!c.IsBuildable(ix, iy, warehouseW))
        {
            bool placed = false;
            for (float frac = 0.45f; frac <= 0.85f; frac += 0.20f)
            {
                float candX = cx - (sx * c.Radius * frac);
                float candY = cy - (sy * c.Radius * frac);
                if (c.IsBuildable(candX, candY, warehouseW))
                {
                    ix = candX;
                    iy = candY;
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                ix = cx;
                iy = cy;
            }
        }

        if (c.IsBuildable(ix, iy, warehouseW))
        {
            c.Add(new LandmarkPiece(LandmarkPieceKind.Warehouse, ix, iy, warehouseW, (byte)(c.Hash(1) % 6)));
        }

        byte houseStyle = c.Level == 0 && c.Poi.TileY != 0 ? LandmarkPiece.StyleTemperate : LandmarkPiece.StyleTemperate;
        for (int i = -1; i <= 1; i += 2)
        {
            float x = ix + (px * i * 220f * Px) + (sx * 20f * Px);
            float y = iy + (py * i * 220f * Px) + (sy * 20f * Px);
            if (c.IsBuildable(x, y, 144f * Px))
            {
                c.Add(new LandmarkPiece(LandmarkPieceKind.House, x, y, 144f * Px, (byte)((c.Hash(i + 5) % 6) | (uint)(houseStyle << 4))));
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

/// <summary>Overgrown monoliths, broken stone pillars, and an ancient altar.</summary>
internal sealed class AncientRuinsPlanner : ILandmarkPlanner
{
    public void Plan(LandmarkPlanContext c)
    {
        float cx = c.CenterX;
        float cy = c.CenterY;
        c.Add(new LandmarkPiece(LandmarkPieceKind.Altar, cx, cy, 1f, 1));

        for (int i = 0; i < 6; i++)
        {
            float a = (i * (MathF.Tau / 6f)) + (c.Hash01(i) * 0.25f);
            float dist = 4.2f + (c.Hash01(i + 20) * 2.5f);
            float x = cx + (LandmarkPlanContext.Cos(a) * dist);
            float y = cy + (LandmarkPlanContext.Sin(a) * dist * 0.85f);
            if (c.IsBuildable(x, y, 1f))
            {
                c.Add(new LandmarkPiece(LandmarkPieceKind.RuinedPillar, x, y, 1f, (byte)(c.Hash(i + 40) % 3)));
            }
        }

        c.Add(new LandmarkPiece(LandmarkPieceKind.StoneLantern, cx - 2.2f, cy + 1.5f, 1f, 1));
        c.Add(new LandmarkPiece(LandmarkPieceKind.StoneLantern, cx + 2.2f, cy + 1.5f, 1f, 1));
    }
}

/// <summary>Outlaw bandit camp featuring a watchtower, central bonfire, tents, and stolen barrels.</summary>
internal sealed class BanditCampPlanner : ILandmarkPlanner
{
    public void Plan(LandmarkPlanContext c)
    {
        const float Px = LandmarkPlanContext.Px;
        float cx = c.CenterX;
        float cy = c.CenterY;
        c.Add(new LandmarkPiece(LandmarkPieceKind.Campfire, cx, cy, 1f, 0));

        float towerX = cx - 4.0f;
        float towerY = cy - 2.5f;
        if (c.IsBuildable(towerX, towerY, 2f))
        {
            c.Add(new LandmarkPiece(LandmarkPieceKind.Watchtower, towerX, towerY, 2f, 0));
        }

        for (int i = 0; i < 4; i++)
        {
            float a = (i * (MathF.Tau / 4f)) + 0.4f + (c.Hash01(i + 10) * 0.2f);
            float x = cx + (LandmarkPlanContext.Cos(a) * 3.8f);
            float y = cy + (LandmarkPlanContext.Sin(a) * 3.2f);
            if (c.IsBuildable(x, y, 56f * Px) && !c.Overlaps(x, y, 1.8f))
            {
                c.Add(new LandmarkPiece(LandmarkPieceKind.Tent, x, y, 58f * Px, (byte)(c.Hash(i + 50) % 3)));
            }
        }

        c.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, cx + 2.0f, cy + 1.4f, 1f, 0));
        c.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, cx + 2.9f, cy + 1.1f, 1f, 1));
    }
}

/// <summary>Timber frontier watchtower with supply barrels and lantern.</summary>
internal sealed class WatchtowerPlanner : ILandmarkPlanner
{
    public void Plan(LandmarkPlanContext c)
    {
        float cx = c.CenterX;
        float cy = c.CenterY;
        c.Add(new LandmarkPiece(LandmarkPieceKind.Watchtower, cx, cy, 2f, (byte)(c.Hash(2) % 2)));
        c.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, cx + 1.8f, cy + 0.4f, 1f, 0));
        c.Add(new LandmarkPiece(LandmarkPieceKind.StoneLantern, cx - 1.6f, cy + 0.5f, 1f, 0));
    }
}

/// <summary>Wide open endgame stone arena surrounded by 8 monolith pillars and torches for boss encounters.</summary>
internal sealed class BossArenaPlanner : ILandmarkPlanner
{
    public void Plan(LandmarkPlanContext c)
    {
        float cx = c.CenterX;
        float cy = c.CenterY;
        c.Add(new LandmarkPiece(LandmarkPieceKind.Altar, cx, cy, 1f, 2));

        for (int i = 0; i < 8; i++)
        {
            float a = i * (MathF.Tau / 8f);
            float px = cx + (LandmarkPlanContext.Cos(a) * 11.2f);
            float py = cy + (LandmarkPlanContext.Sin(a) * 10.2f);
            c.Add(new LandmarkPiece((i & 1) == 0 ? LandmarkPieceKind.RuinedPillar : LandmarkPieceKind.StoneLantern, px, py, 1f, 1));
        }
    }
}
