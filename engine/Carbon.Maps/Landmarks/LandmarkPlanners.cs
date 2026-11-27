// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Biomes;
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
    private readonly List<LandmarkPiece> _pieces = new();
    private readonly List<LandmarkField> _fields = new();
    private readonly List<VillagePlot> _plots = new();

    internal LandmarkPlanContext(in PointOfInterest poi, ITileSource tiles, Func<int, int, bool>? isRoad = null)
    {
        _tiles = tiles;
        _isRoad = isRoad;
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

    /// <summary>Gets the village building plots placed so far.</summary>
    public IReadOnlyList<VillagePlot> Plots => _plots;

    /// <summary>Adds a structure.</summary>
    public void Add(LandmarkPiece piece) => _pieces.Add(piece);

    /// <summary>Adds a village building plot (built from real building-grid structures, ADR-025).</summary>
    public void Add(VillagePlot plot) => _plots.Add(plot);

    /// <summary>Samples the pure terrain at a world tile.</summary>
    public MapTile SampleTile(int tileX, int tileY) => _tiles.SampleTile(tileX, tileY);

    /// <summary>Returns whether a road or bridge runs over a world tile (always <c>false</c> without a road network).</summary>
    public bool IsRoad(int tileX, int tileY) => _isRoad?.Invoke(tileX, tileY) ?? false;

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

    internal LandmarkLayout ToLayout() => new([.. _pieces], [.. _fields], [.. _plots]);
}

/// <summary>
/// A well on a small square, real buildings from <c>village_buildings.json</c> around it (a market stall, the smithy, the
/// general store and houses, every entrance facing the square), optional crop fields and a barrel (ADR-025).
/// </summary>
internal sealed class VillagePlanner(VillageStyle style, bool withFields) : ILandmarkPlanner
{
    /// <summary>Most houses a village gets.</summary>
    public const int MaxHouses = 5;

    private const int Attempts = 40;

    // Required buildings first, so a cramped site still gets its traders and smith.
    private static readonly VillageBuildingRole[] s_order =
    [
        VillageBuildingRole.Stall,
        VillageBuildingRole.Smithy,
        VillageBuildingRole.Store,
        VillageBuildingRole.House,
        VillageBuildingRole.House,
        VillageBuildingRole.House,
        VillageBuildingRole.House,
        VillageBuildingRole.House,
        VillageBuildingRole.House,
        VillageBuildingRole.House,
    ];

    public void Plan(LandmarkPlanContext c)
    {
        const float Px = LandmarkPlanContext.Px;
        float cx = c.CenterX;
        float cy = c.CenterY;
        float r = c.Radius;
        c.Add(new LandmarkPiece(LandmarkPieceKind.Well, cx, cy + (10f * Px), 1f, 0));

        VillageBuildingCatalog catalog = VillageBuildingCatalog.Default;
        int houses = 0;
        for (int k = 0; k < s_order.Length && houses < MaxHouses; k++)
        {
            VillageBuildingRole role = s_order[k];
            int templateIndex = catalog.Pick(role, style, c.Hash(300 + k));
            if (templateIndex >= 0 && this.TryPlace(c, catalog, templateIndex, role, k) && role == VillageBuildingRole.House)
            {
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
            bool clearOfBuildings = !c.OverlapsPlot(
                (int)MathF.Floor(fx - hx), (int)MathF.Floor(fy - hy), (int)MathF.Floor(fx + hx), (int)MathF.Floor(fy + hy), margin: 1);
            if (clearOfBuildings && c.IsBuildable(fx, fy, hx * 2f) && !c.Overlaps(fx, fy, hx + (40f * Px)))
            {
                c.Add(new LandmarkField(fx - hx, fy - hy, fx + hx, fy + hy, (byte)(c.Hash(i + 230) % 3)));
            }
        }

        float barrelX = cx + (42f * Px);
        float barrelY = cy + (34f * Px);
        if (!c.OverlapsPlot((int)MathF.Floor(barrelX) - 1, (int)MathF.Floor(barrelY), (int)MathF.Floor(barrelX), (int)MathF.Floor(barrelY), margin: 0))
        {
            c.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, barrelX, barrelY, 1f, 0));
        }
    }

    private bool TryPlace(LandmarkPlanContext c, VillageBuildingCatalog catalog, int templateIndex, VillageBuildingRole role, int order)
    {
        VillageBuildingTemplate template = catalog.Templates[templateIndex];
        int centerX = c.Poi.TileX;
        int centerY = c.Poi.TileY;
        float start = (order * 2.39996f) + (c.Hash01(400 + order) * 0.6f);

        // Pass 0 turns every entrance to the square; pass 1 (a cramped or broken site) accepts any facing.
        for (int pass = 0; pass < 2; pass++)
        {
            for (int j = 0; j < Attempts; j++)
            {
                float a = start + (j * (MathF.Tau / Attempts));
                float dist = role == VillageBuildingRole.Stall
                    ? 4.5f + (j % 2)
                    : 6.5f + (((j * 3) + order) % 7);
                float bx = c.CenterX + (LandmarkPlanContext.Cos(a) * dist);
                float by = c.CenterY + (LandmarkPlanContext.Sin(a) * dist);

                // The entrance faces the square: pick the side pointing back at the well.
                float dx = c.CenterX - bx;
                float dy = c.CenterY - by;
                int facing = MathF.Abs(dx) > MathF.Abs(dy) ? (dx > 0f ? 3 : 1) : (dy > 0f ? 0 : 2);

                for (int t = 0; t < (pass == 0 ? 1 : 4); t++)
                {
                    int turns = (facing + t) & 3;
                    (int w, int h) = template.RotatedSize(turns);
                    int ox = (int)MathF.Floor(bx - (w * 0.5f));
                    int oy = (int)MathF.Floor(by - (h * 0.5f));
                    VillagePlot plot = new((ushort)templateIndex, role, style, ox, oy, (byte)turns, (byte)w, (byte)h);
                    if (this.IsValid(c, catalog, plot, centerX, centerY))
                    {
                        c.Add(plot);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool IsValid(LandmarkPlanContext c, VillageBuildingCatalog catalog, in VillagePlot plot, int centerX, int centerY)
    {
        int minX = plot.OriginX;
        int minY = plot.OriginY;
        int maxX = minX + plot.Width - 1;
        int maxY = minY + plot.Height - 1;
        float limit = c.Radius - 0.5f;

        // Inside the cleared footprint, off the well square and at least one tile from every other building.
        if (!WithinRadius(minX, minY, centerX, centerY, limit) || !WithinRadius(maxX, minY, centerX, centerY, limit) ||
            !WithinRadius(minX, maxY, centerX, centerY, limit) || !WithinRadius(maxX, maxY, centerX, centerY, limit))
        {
            return false;
        }

        if (minX <= centerX + 2 && maxX >= centerX - 2 && minY <= centerY + 2 && maxY >= centerY - 2)
        {
            return false;
        }

        if (c.OverlapsPlot(minX, minY, maxX, maxY, margin: 1))
        {
            return false;
        }

        for (int y = minY - 1; y <= maxY + 1; y++)
        {
            for (int x = minX - 1; x <= maxX + 1; x++)
            {
                bool inside = x >= minX && x <= maxX && y >= minY && y <= maxY;
                if (!c.IsBuildableTile(x, y) || (inside && c.IsRoad(x, y)))
                {
                    return false;
                }
            }
        }

        (int fx, int fy) = VillageBlueprint.FrontTile(catalog, plot);
        return c.IsBuildableTile(fx, fy);
    }

    private static bool WithinRadius(int x, int y, int centerX, int centerY, float limit)
    {
        float ex = x + 0.5f - (centerX + 0.5f);
        float ey = y + 0.5f - (centerY + 0.5f);
        return (ex * ex) + (ey * ey) <= limit * limit;
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
