// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Landmarks;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Procedural 2.5D art for points of interest, in the same style as the terrain and props
/// (top-down 3/4 view, soft drop shadows, lit top faces, darker front faces).
/// </summary>
/// <remarks>
/// Each point of interest gets a deterministic layout (built once and cached) that respects the terrain:
/// buildings are only placed on dry, buildable tiles at the site's height level. Rendering is split into a
/// flat ground pass (paths, plaza, fields, piers, boats) drawn under everything, and upright pieces
/// (houses, well, tents, gate, lanterns) that join the Y-sorted pass with props and entities.
/// </remarks>
internal sealed class PointOfInterestRenderer
{
    private readonly float _tileSize;
    private readonly Func<int, int, MapTile> _tileAt;
    private readonly Dictionary<long, Layout> _layouts = new();

    public PointOfInterestRenderer(float tileSize, Func<int, int, MapTile> tileAt)
    {
        _tileSize = tileSize;
        _tileAt = tileAt;
    }

    /// <summary>Kind of an upright piece that takes part in Y-sorting.</summary>
    public enum PieceKind : byte
    {
        House,
        Warehouse,
        Well,
        Tent,
        Campfire,
        ShrineGate,
        StoneLantern,
        Altar,
        Barrel,
    }

    /// <summary>An upright piece of a point of interest. <see cref="Position"/> is its ground contact point.</summary>
    public readonly record struct Piece(PieceKind Kind, Vector2 Position, float Width, byte Variant);

    /// <summary>Drops cached layouts (e.g. after the world is regenerated).</summary>
    public void Clear() => _layouts.Clear();

    /// <summary>Draws the flat ground layer of a point of interest.</summary>
    public void DrawGround(GeometryBatch batch, in PointOfInterest poi)
    {
        Layout layout = this.GetLayout(poi);
        Vector2 c = this.Centre(poi);
        float r = poi.Radius * _tileSize;

        switch (poi.Type)
        {
            case PointOfInterestType.RiverVillage:
                DrawFields(batch, layout);
                foreach (Piece p in layout.Pieces)
                {
                    if (p.Kind == PieceKind.House)
                    {
                        DrawPath(batch, c, p.Position + new Vector2(0f, 6f), 14f);
                    }
                }

                DrawPlaza(batch, c, r * 0.30f);
                break;

            case PointOfInterestType.Harbor:
                DrawPlaza(batch, c, r * 0.35f);
                this.DrawPiers(batch, poi, c, r);
                break;

            case PointOfInterestType.LakesideCamp:
                batch.FillCircle(c, r * 0.55f, new ColorRgba(150, 128, 88, 150), 28);
                batch.FillCircle(c, r * 0.40f, new ColorRgba(170, 146, 100, 170), 28);
                break;

            case PointOfInterestType.MountainShrine:
                DrawShrinePlatform(batch, c, r * 0.62f);
                break;

            case PointOfInterestType.None:
            default:
                break;
        }
    }

    /// <summary>Gets the upright pieces of a point of interest, for Y-sorting.</summary>
    public IReadOnlyList<Piece> GetPieces(in PointOfInterest poi) => this.GetLayout(poi).Pieces;

    /// <summary>Draws one upright piece.</summary>
    public static void DrawPiece(GeometryBatch batch, in Piece piece)
    {
        switch (piece.Kind)
        {
            case PieceKind.House:
                DrawHouse(batch, piece.Position, piece.Width, piece.Variant, isWarehouse: false);
                break;
            case PieceKind.Warehouse:
                DrawHouse(batch, piece.Position, piece.Width, piece.Variant, isWarehouse: true);
                break;
            case PieceKind.Well:
                DrawWell(batch, piece.Position);
                break;
            case PieceKind.Tent:
                DrawTent(batch, piece.Position, piece.Width, piece.Variant);
                break;
            case PieceKind.Campfire:
                DrawCampfire(batch, piece.Position);
                break;
            case PieceKind.ShrineGate:
                DrawShrineGate(batch, piece.Position, piece.Width);
                break;
            case PieceKind.StoneLantern:
                DrawStoneLantern(batch, piece.Position);
                break;
            case PieceKind.Altar:
                DrawAltar(batch, piece.Position);
                break;
            case PieceKind.Barrel:
                DrawBarrels(batch, piece.Position);
                break;
            default:
                break;
        }
    }

    // ── Layout ───────────────────────────────────────────────────────────────

    private sealed class Layout
    {
        public List<Piece> Pieces { get; } = new();

        public List<(Vector2 Min, Vector2 Max, byte Crop)> Fields { get; } = new();
    }

    private Vector2 Centre(in PointOfInterest poi) =>
        new((poi.TileX + 0.5f) * _tileSize, (poi.TileY + 0.5f) * _tileSize);

    private Layout GetLayout(in PointOfInterest poi)
    {
        long key = ((long)poi.TileX << 32) | (uint)poi.TileY;
        if (!_layouts.TryGetValue(key, out Layout? layout))
        {
            layout = this.BuildLayout(poi);
            _layouts[key] = layout;
        }

        return layout;
    }

    private Layout BuildLayout(in PointOfInterest poi)
    {
        Layout layout = new();
        Vector2 c = this.Centre(poi);
        float r = poi.Radius * _tileSize;
        byte level = _tileAt(poi.TileX, poi.TileY).HeightLevel;
        uint seed = (uint)((poi.TileX * 73856093) ^ (poi.TileY * 19349663));

        switch (poi.Type)
        {
            case PointOfInterestType.RiverVillage:
                {
                    layout.Pieces.Add(new Piece(PieceKind.Well, c + new Vector2(0f, 10f), 0f, 0));
                    int houses = 0;
                    for (int i = 0; i < 9 && houses < 7; i++)
                    {
                        float a = (i * (MathF.Tau / 9f)) + (Hash01(seed, i) * 0.35f);
                        float dist = r * (0.55f + (Hash01(seed, i + 40) * 0.22f));
                        Vector2 pos = c + new Vector2(MathF.Cos(a) * dist, MathF.Sin(a) * dist * 0.85f);
                        float w = 64f + (Hash01(seed, i + 80) * 26f);
                        if (this.IsBuildable(pos, w, level) && !Overlaps(layout.Pieces, pos, w + 22f))
                        {
                            layout.Pieces.Add(new Piece(PieceKind.House, pos, w, (byte)(Hash(seed, i + 120) % 6)));
                            houses++;
                        }
                    }

                    // Crop fields on the open ground between the houses.
                    for (int i = 0; i < 6; i++)
                    {
                        float a = (i * (MathF.Tau / 6f)) + 0.5f + (Hash01(seed, i + 200) * 0.3f);
                        Vector2 fc = c + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * (r * 0.86f));
                        Vector2 half = new(36f + (Hash01(seed, i + 210) * 18f), 22f + (Hash01(seed, i + 220) * 12f));
                        if (this.IsBuildable(fc, half.X * 2f, level) && !Overlaps(layout.Pieces, fc, half.X + 40f))
                        {
                            layout.Fields.Add((fc - half, fc + half, (byte)(Hash(seed, i + 230) % 3)));
                        }
                    }

                    layout.Pieces.Add(new Piece(PieceKind.Barrel, c + new Vector2(42f, 34f), 0f, 0));
                    break;
                }

            case PointOfInterestType.Harbor:
                {
                    Vector2 sea = SeaDirection(poi);
                    Vector2 inland = c - (sea * (r * 0.30f));
                    layout.Pieces.Add(new Piece(PieceKind.Warehouse, inland, 104f, (byte)(Hash(seed, 1) % 6)));
                    Vector2 side = new(-sea.Y, sea.X);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        Vector2 pos = inland + (side * (i * 120f)) + (sea * 20f);
                        if (this.IsBuildable(pos, 64f, level))
                        {
                            layout.Pieces.Add(new Piece(PieceKind.House, pos, 64f, (byte)(Hash(seed, i + 5) % 6)));
                        }
                    }

                    layout.Pieces.Add(new Piece(PieceKind.Barrel, c + (side * 46f) + (sea * 30f), 0f, 0));
                    layout.Pieces.Add(new Piece(PieceKind.Barrel, c - (side * 52f) + (sea * 36f), 0f, 1));
                    break;
                }

            case PointOfInterestType.LakesideCamp:
                {
                    layout.Pieces.Add(new Piece(PieceKind.Campfire, c, 0f, 0));
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (-MathF.PI / 2f) + ((i - 1) * 1.1f) + (Hash01(seed, i) * 0.3f);
                        Vector2 pos = c + new Vector2(MathF.Cos(a) * 76f, MathF.Sin(a) * 62f);
                        if (this.IsBuildable(pos, 56f, level))
                        {
                            layout.Pieces.Add(new Piece(PieceKind.Tent, pos, 56f + (Hash01(seed, i + 9) * 10f), (byte)(Hash(seed, i + 20) % 3)));
                        }
                    }

                    break;
                }

            case PointOfInterestType.MountainShrine:
                layout.Pieces.Add(new Piece(PieceKind.Altar, c + new Vector2(0f, -24f), 0f, 0));
                layout.Pieces.Add(new Piece(PieceKind.ShrineGate, c + new Vector2(0f, r * 0.62f), 92f, 0));
                layout.Pieces.Add(new Piece(PieceKind.StoneLantern, c + new Vector2(-58f, 18f), 0f, 0));
                layout.Pieces.Add(new Piece(PieceKind.StoneLantern, c + new Vector2(58f, 18f), 0f, 0));
                break;

            case PointOfInterestType.None:
            default:
                break;
        }

        return layout;
    }

    // Dry ground at the site's level under the whole footprint: no houses in rivers or on cliff edges.
    private bool IsBuildable(Vector2 groundPoint, float width, byte level)
    {
        for (int i = -1; i <= 1; i++)
        {
            for (int j = 0; j <= 1; j++)
            {
                int tx = (int)MathF.Floor((groundPoint.X + (i * width * 0.5f)) / _tileSize);
                int ty = (int)MathF.Floor((groundPoint.Y - (j * 40f)) / _tileSize);
                MapTile t = _tileAt(tx, ty);
                if (t.IsSolid || t.HeightLevel != level || t.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Overlaps(List<Piece> pieces, Vector2 p, float clearance)
    {
        foreach (Piece piece in pieces)
        {
            if (Vector2.DistanceSquared(piece.Position, p) < clearance * clearance)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector2 SeaDirection(in PointOfInterest poi)
    {
        Vector2 d = new(poi.FacingX, poi.FacingY);
        return d.LengthSquared() > 0.01f ? Vector2.Normalize(d) : new Vector2(0f, 1f);
    }

    private static uint Hash(uint seed, int i)
    {
        uint h = seed ^ (uint)(i * 0x9E3779B9);
        h ^= h >> 16;
        h *= 0x7FEB352D;
        h ^= h >> 15;
        h *= 0x846CA68B;
        return h ^ (h >> 16);
    }

    private static float Hash01(uint seed, int i) => (Hash(seed, i) & 0xFFFF) / 65535f;

    // ── Ground layer ─────────────────────────────────────────────────────────

    private static void DrawPlaza(GeometryBatch batch, Vector2 c, float radius)
    {
        batch.FillCircle(c + new Vector2(0f, 3f), radius + 4f, new ColorRgba(0, 0, 0, 30), 40);
        batch.FillCircle(c, radius + 4f, new ColorRgba(120, 104, 80), 40);
        batch.FillCircle(c, radius, new ColorRgba(176, 160, 132), 40);

        // Cobblestones: a few concentric rings of darker stones.
        for (int ring = 1; ring <= 3; ring++)
        {
            float rr = radius * ring / 4f;
            int stones = 6 + (ring * 6);
            for (int i = 0; i < stones; i++)
            {
                float a = (i + (ring * 0.5f)) * (MathF.Tau / stones);
                Vector2 p = c + new Vector2(MathF.Cos(a) * rr, MathF.Sin(a) * rr);
                batch.FillRectangle(new RectangleF(p.X - 3f, p.Y - 2f, 6f, 4f), new ColorRgba(150, 134, 108));
            }
        }
    }

    private static void DrawPath(GeometryBatch batch, Vector2 from, Vector2 to, float width)
    {
        batch.DrawLine(from, to, new ColorRgba(140, 112, 74), width + 4f);
        batch.DrawLine(from, to, new ColorRgba(184, 156, 108), width);
    }

    private static void DrawFields(GeometryBatch batch, Layout layout)
    {
        foreach ((Vector2 min, Vector2 max, byte crop) in layout.Fields)
        {
            ColorRgba soil = new(112, 78, 46);
            ColorRgba row = crop switch
            {
                0 => new ColorRgba(214, 178, 74),  // wheat
                1 => new ColorRgba(88, 152, 62),   // vegetables
                _ => new ColorRgba(150, 110, 170), // lavender
            };

            batch.FillRectangle(new RectangleF(min.X - 3f, min.Y - 3f, max.X - min.X + 6f, max.Y - min.Y + 6f), new ColorRgba(92, 64, 40));
            batch.FillRectangle(new RectangleF(min.X, min.Y, max.X - min.X, max.Y - min.Y), soil);
            for (float y = min.Y + 3f; y < max.Y - 2f; y += 7f)
            {
                batch.FillRectangle(new RectangleF(min.X + 2f, y, max.X - min.X - 4f, 4f), row);
            }

            // Fence posts along the north and south edges.
            for (float x = min.X; x <= max.X; x += 12f)
            {
                batch.FillRectangle(new RectangleF(x - 1.5f, min.Y - 8f, 3f, 8f), new ColorRgba(110, 74, 42));
                batch.FillRectangle(new RectangleF(x - 1.5f, max.Y - 2f, 3f, 8f), new ColorRgba(110, 74, 42));
            }

            batch.DrawLine(new Vector2(min.X, min.Y - 6f), new Vector2(max.X, min.Y - 6f), new ColorRgba(140, 100, 60), 2f);
            batch.DrawLine(new Vector2(min.X, max.Y + 2f), new Vector2(max.X, max.Y + 2f), new ColorRgba(140, 100, 60), 2f);
        }
    }

    private void DrawPiers(GeometryBatch batch, in PointOfInterest poi, Vector2 c, float r)
    {
        Vector2 sea = SeaDirection(poi);
        Vector2 side = new(-sea.Y, sea.X);
        ColorRgba plank = new(150, 104, 60);
        ColorRgba plankDark = new(112, 76, 44);
        ColorRgba post = new(84, 56, 34);

        for (int i = -1; i <= 1; i += 2)
        {
            Vector2 start = c + (side * (i * 52f));
            Vector2 end = start + (sea * (r * 1.55f));
            float len = Vector2.Distance(start, end);
            int planks = (int)(len / 10f);

            // Shadow on the water, then the planks across the pier.
            batch.DrawLine(start + new Vector2(5f, 6f), end + new Vector2(5f, 6f), new ColorRgba(0, 0, 0, 50), 30f);
            for (int k = 0; k < planks; k++)
            {
                Vector2 p = start + (sea * (k * 10f));
                batch.DrawLine(p - (side * 13f), p + (side * 13f), (k & 1) == 0 ? plank : plankDark, 8f);
            }

            // Mooring posts and a moored boat.
            for (int k = 0; k <= planks; k += 6)
            {
                Vector2 p = start + (sea * (k * 10f));
                batch.FillCircle(p + (side * 15f), 3.5f, post, 8);
                batch.FillCircle(p - (side * 15f), 3.5f, post, 8);
            }

            Vector2 boat = start + (sea * (len * 0.65f)) + (side * (i * 46f));
            DrawBoat(batch, boat, sea, (byte)(i + 1));
        }
    }

    private static void DrawBoat(GeometryBatch batch, Vector2 c, Vector2 forward, byte variant)
    {
        Vector2 side = new(-forward.Y, forward.X);
        Span<Vector2> hull =
        [
            c + (forward * 34f),
            c + (forward * 14f) + (side * 13f),
            c - (forward * 26f) + (side * 12f),
            c - (forward * 30f),
            c - (forward * 26f) - (side * 12f),
            c + (forward * 14f) - (side * 13f),
        ];
        Span<Vector2> shadow = stackalloc Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            shadow[i] = hull[i] + new Vector2(4f, 5f);
        }

        batch.FillConvexPolygon(shadow, new ColorRgba(0, 0, 0, 55));
        batch.FillConvexPolygon(hull, new ColorRgba(110, 66, 36));
        for (int i = 0; i < 6; i++)
        {
            hull[i] = c + ((hull[i] - c) * 0.72f);
        }

        batch.FillConvexPolygon(hull, new ColorRgba(170, 120, 72));
        batch.DrawLine(c - (side * 9f), c + (side * 9f), new ColorRgba(120, 80, 46), 3f);

        // Sail
        ColorRgba sail = variant == 0 ? new ColorRgba(244, 238, 220) : new ColorRgba(220, 120, 90);
        batch.FillTriangle(c + new Vector2(0f, -46f), c + new Vector2(0f, -6f), c + new Vector2(22f, -10f), sail);
        batch.DrawLine(c, c + new Vector2(0f, -48f), new ColorRgba(90, 60, 36), 2.5f);
    }

    private static void DrawShrinePlatform(GeometryBatch batch, Vector2 c, float radius)
    {
        Span<Vector2> octagon = stackalloc Vector2[8];
        static void Octagon(Span<Vector2> pts, Vector2 centre, float rr)
        {
            for (int i = 0; i < 8; i++)
            {
                float a = (i + 0.5f) * (MathF.Tau / 8f);
                pts[i] = centre + new Vector2(MathF.Cos(a) * rr, MathF.Sin(a) * rr * 0.8f);
            }
        }

        Octagon(octagon, c + new Vector2(6f, 10f), radius + 8f);
        batch.FillConvexPolygon(octagon, new ColorRgba(0, 0, 0, 50));
        Octagon(octagon, c + new Vector2(0f, 8f), radius + 6f);
        batch.FillConvexPolygon(octagon, new ColorRgba(92, 100, 112)); // step riser
        Octagon(octagon, c, radius + 6f);
        batch.FillConvexPolygon(octagon, new ColorRgba(150, 158, 170));
        Octagon(octagon, c, radius * 0.72f);
        batch.FillConvexPolygon(octagon, new ColorRgba(176, 184, 194));

        // Flagstone joints
        for (int i = 0; i < 8; i++)
        {
            float a = (i + 0.5f) * (MathF.Tau / 8f);
            batch.DrawLine(c, c + new Vector2(MathF.Cos(a) * radius, MathF.Sin(a) * radius * 0.8f), new ColorRgba(132, 140, 152), 1.5f);
        }
    }

    // ── Upright pieces ───────────────────────────────────────────────────────

    private static readonly ColorRgba[] s_roofs =
    [
        new(196, 150, 72),  // thatch
        new(168, 64, 48),   // red tile
        new(78, 98, 128),   // slate
    ];

    private static readonly ColorRgba[] s_walls =
    [
        new(236, 224, 198),
        new(222, 204, 168),
    ];

    private static void DrawHouse(GeometryBatch batch, Vector2 foot, float width, byte variant, bool isWarehouse)
    {
        ColorRgba roof = s_roofs[variant % s_roofs.Length];
        ColorRgba wall = isWarehouse ? new ColorRgba(150, 106, 66) : s_walls[variant / 3 % s_walls.Length];
        float wallH = isWarehouse ? 36f : 28f;
        float depth = isWarehouse ? 48f : 40f;
        float x0 = foot.X - (width * 0.5f);
        float top = foot.Y - wallH;

        // Drop shadow to the south-east, matching the trees.
        batch.FillRectangle(new RectangleF(x0 + 8f, foot.Y - 6f, width + 6f, 14f), new ColorRgba(0, 0, 0, 55));

        // Front wall with timber frame
        batch.FillRectangle(new RectangleF(x0, top, width, wallH), wall);
        batch.FillRectangle(new RectangleF(x0, foot.Y - 4f, width, 4f), new ColorRgba(120, 104, 88)); // stone footing
        ColorRgba timber = new(102, 66, 40);
        batch.FillRectangle(new RectangleF(x0, top, 4f, wallH), timber);
        batch.FillRectangle(new RectangleF(x0 + width - 4f, top, 4f, wallH), timber);
        batch.FillRectangle(new RectangleF(x0, top, width, 3f), timber);

        if (isWarehouse)
        {
            // Wide double door
            batch.FillRectangle(new RectangleF(foot.X - 16f, foot.Y - 26f, 32f, 26f), new ColorRgba(84, 52, 30));
            batch.DrawLine(new Vector2(foot.X, foot.Y - 26f), new Vector2(foot.X, foot.Y), new ColorRgba(60, 36, 20), 2f);
            batch.DrawLine(new Vector2(foot.X - 16f, foot.Y - 26f), new Vector2(foot.X + 16f, foot.Y), new ColorRgba(60, 36, 20), 2f);
        }
        else
        {
            batch.FillRectangle(new RectangleF(foot.X - 7f, foot.Y - 20f, 14f, 20f), new ColorRgba(92, 58, 34));
            batch.FillRectangle(new RectangleF(foot.X + 3f, foot.Y - 11f, 2f, 2f), new ColorRgba(230, 190, 90));
            ColorRgba glass = (variant & 1) == 0 ? new ColorRgba(255, 214, 120) : new ColorRgba(150, 196, 220);
            for (int i = -1; i <= 1; i += 2)
            {
                float wx = foot.X + (i * width * 0.28f) - 6f;
                batch.FillRectangle(new RectangleF(wx - 1f, top + 7f, 14f, 12f), timber);
                batch.FillRectangle(new RectangleF(wx + 1f, top + 9f, 10f, 8f), glass);
                batch.FillRectangle(new RectangleF(wx + 5.5f, top + 9f, 1f, 8f), timber);
            }
        }

        // Gable roof seen from above: lit front slope, shaded back slope, ridge, eaves overhang.
        float overhang = 6f;
        float front = depth * 0.58f;
        float back = depth * 0.42f;
        ColorRgba roofDark = Shade(roof, 0.72f);
        ColorRgba roofLine = Shade(roof, 0.82f);
        batch.FillRectangle(new RectangleF(x0 - overhang, top - front - back, width + (2f * overhang), back), roofDark);
        batch.FillRectangle(new RectangleF(x0 - overhang, top - front, width + (2f * overhang), front + 4f), roof);
        for (float y = top - front + 6f; y < top + 2f; y += 6f)
        {
            batch.FillRectangle(new RectangleF(x0 - overhang, y, width + (2f * overhang), 1.5f), roofLine);
        }

        batch.FillRectangle(new RectangleF(x0 - overhang, top - front - 2f, width + (2f * overhang), 4f), Shade(roof, 1.15f));
        batch.FillRectangle(new RectangleF(x0 - overhang, top + 2f, width + (2f * overhang), 2f), Shade(roof, 0.55f));

        // Chimney with a wisp of smoke
        if (!isWarehouse && (variant % 2) == 0)
        {
            float cx = x0 + (width * 0.72f);
            batch.FillRectangle(new RectangleF(cx, top - front - back + 4f, 9f, 16f), new ColorRgba(120, 96, 84));
            batch.FillRectangle(new RectangleF(cx - 1f, top - front - back + 2f, 11f, 3f), new ColorRgba(90, 72, 64));
            batch.FillCircle(new Vector2(cx + 6f, top - front - back - 6f), 5f, new ColorRgba(220, 220, 220, 110), 10);
            batch.FillCircle(new Vector2(cx + 11f, top - front - back - 16f), 7f, new ColorRgba(220, 220, 220, 70), 10);
        }
    }

    private static void DrawWell(GeometryBatch batch, Vector2 foot)
    {
        batch.FillCircle(foot + new Vector2(5f, 2f), 18f, new ColorRgba(0, 0, 0, 55), 20);
        batch.FillRectangle(new RectangleF(foot.X - 16f, foot.Y - 14f, 32f, 14f), new ColorRgba(120, 124, 132)); // stone drum front
        batch.FillCircle(foot + new Vector2(0f, -14f), 16f, new ColorRgba(150, 156, 166), 20);
        batch.FillCircle(foot + new Vector2(0f, -14f), 11f, new ColorRgba(40, 84, 130), 20);
        batch.FillCircle(foot + new Vector2(-3f, -16f), 4f, new ColorRgba(120, 180, 220), 10);

        // Posts and a little roof
        ColorRgba wood = new(100, 66, 40);
        batch.FillRectangle(new RectangleF(foot.X - 17f, foot.Y - 46f, 4f, 34f), wood);
        batch.FillRectangle(new RectangleF(foot.X + 13f, foot.Y - 46f, 4f, 34f), wood);
        batch.DrawLine(new Vector2(foot.X - 15f, foot.Y - 36f), new Vector2(foot.X + 15f, foot.Y - 36f), wood, 3f);
        batch.FillTriangle(new Vector2(foot.X - 24f, foot.Y - 44f), new Vector2(foot.X + 24f, foot.Y - 44f), new Vector2(foot.X, foot.Y - 60f), new ColorRgba(168, 64, 48));
        batch.FillTriangle(new Vector2(foot.X - 24f, foot.Y - 44f), new Vector2(foot.X, foot.Y - 44f), new Vector2(foot.X, foot.Y - 60f), new ColorRgba(140, 52, 40));
    }

    private static void DrawTent(GeometryBatch batch, Vector2 foot, float width, byte variant)
    {
        ColorRgba canvas = variant switch
        {
            0 => new ColorRgba(240, 234, 216),
            1 => new ColorRgba(120, 148, 104),
            _ => new ColorRgba(176, 104, 80),
        };
        float h = width * 0.8f;
        float half = width * 0.5f;

        batch.FillRectangle(new RectangleF(foot.X - half + 8f, foot.Y - 4f, width + 10f, 10f), new ColorRgba(0, 0, 0, 55));

        // Side slope (running back), then the front triangle and the door flap.
        Span<Vector2> sideSlope =
        [
            new Vector2(foot.X, foot.Y - h),
            new Vector2(foot.X + 10f, foot.Y - h - 18f),
            new Vector2(foot.X + half + 10f, foot.Y - 18f),
            new Vector2(foot.X + half, foot.Y),
        ];
        batch.FillConvexPolygon(sideSlope, Shade(canvas, 0.72f));
        batch.FillTriangle(new Vector2(foot.X - half, foot.Y), new Vector2(foot.X + half, foot.Y), new Vector2(foot.X, foot.Y - h), canvas);
        batch.FillTriangle(new Vector2(foot.X - 9f, foot.Y), new Vector2(foot.X + 9f, foot.Y), new Vector2(foot.X, foot.Y - (h * 0.55f)), new ColorRgba(60, 44, 32));
        batch.DrawLine(new Vector2(foot.X, foot.Y - h - 6f), new Vector2(foot.X, foot.Y - h + 4f), new ColorRgba(90, 60, 36), 2.5f);
        batch.DrawLine(new Vector2(foot.X - half, foot.Y), new Vector2(foot.X, foot.Y - h), Shade(canvas, 0.85f), 2f);
    }

    private static void DrawCampfire(GeometryBatch batch, Vector2 foot)
    {
        batch.FillCircle(foot, 34f, new ColorRgba(255, 160, 60, 40), 24); // glow
        for (int i = 0; i < 8; i++)
        {
            float a = i * (MathF.Tau / 8f);
            batch.FillCircle(foot + new Vector2(MathF.Cos(a) * 12f, MathF.Sin(a) * 9f), 4f, new ColorRgba(120, 124, 130), 8);
        }

        batch.DrawLine(foot + new Vector2(-10f, 4f), foot + new Vector2(10f, -4f), new ColorRgba(92, 58, 32), 5f);
        batch.DrawLine(foot + new Vector2(-10f, -4f), foot + new Vector2(10f, 4f), new ColorRgba(110, 70, 40), 5f);
        batch.FillTriangle(foot + new Vector2(-8f, 0f), foot + new Vector2(8f, 0f), foot + new Vector2(0f, -22f), new ColorRgba(234, 88, 12));
        batch.FillTriangle(foot + new Vector2(-5f, 0f), foot + new Vector2(5f, 0f), foot + new Vector2(1f, -15f), new ColorRgba(251, 191, 36));

        // Log seats
        batch.FillRectangle(new RectangleF(foot.X - 46f, foot.Y + 18f, 26f, 8f), new ColorRgba(110, 70, 40));
        batch.FillRectangle(new RectangleF(foot.X + 20f, foot.Y + 18f, 26f, 8f), new ColorRgba(110, 70, 40));
    }

    private static void DrawShrineGate(GeometryBatch batch, Vector2 foot, float width)
    {
        ColorRgba red = new(190, 40, 32);
        ColorRgba redDark = new(140, 28, 22);
        float half = width * 0.5f;
        float h = 70f;

        batch.FillRectangle(new RectangleF(foot.X - half + 6f, foot.Y - 3f, width, 8f), new ColorRgba(0, 0, 0, 50));
        batch.FillRectangle(new RectangleF(foot.X - half + 8f, foot.Y - h, 9f, h), red);
        batch.FillRectangle(new RectangleF(foot.X + half - 17f, foot.Y - h, 9f, h), red);
        batch.FillRectangle(new RectangleF(foot.X - half + 14f, foot.Y - h, 3f, h), redDark);
        batch.FillRectangle(new RectangleF(foot.X + half - 11f, foot.Y - h, 3f, h), redDark);
        batch.FillRectangle(new RectangleF(foot.X - half + 2f, foot.Y - h + 14f, width - 4f, 6f), red);
        batch.FillRectangle(new RectangleF(foot.X - half - 6f, foot.Y - h - 4f, width + 12f, 8f), red);
        batch.FillRectangle(new RectangleF(foot.X - half - 10f, foot.Y - h - 10f, width + 20f, 6f), new ColorRgba(40, 36, 36));
        batch.FillRectangle(new RectangleF(foot.X - 8f, foot.Y - h + 4f, 16f, 10f), new ColorRgba(214, 170, 60)); // plaque
    }

    private static void DrawStoneLantern(GeometryBatch batch, Vector2 foot)
    {
        ColorRgba stone = new(150, 156, 166);
        ColorRgba stoneDark = new(110, 116, 126);
        batch.FillRectangle(new RectangleF(foot.X - 6f, foot.Y - 2f, 18f, 6f), new ColorRgba(0, 0, 0, 50));
        batch.FillRectangle(new RectangleF(foot.X - 8f, foot.Y - 6f, 16f, 6f), stoneDark);
        batch.FillRectangle(new RectangleF(foot.X - 3f, foot.Y - 24f, 6f, 18f), stone);
        batch.FillRectangle(new RectangleF(foot.X - 8f, foot.Y - 36f, 16f, 12f), stoneDark);
        batch.FillRectangle(new RectangleF(foot.X - 4f, foot.Y - 33f, 8f, 6f), new ColorRgba(255, 200, 90));
        batch.FillCircle(new Vector2(foot.X, foot.Y - 30f), 14f, new ColorRgba(255, 190, 80, 45), 14);
        batch.FillTriangle(new Vector2(foot.X - 12f, foot.Y - 36f), new Vector2(foot.X + 12f, foot.Y - 36f), new Vector2(foot.X, foot.Y - 46f), stone);
    }

    private static void DrawAltar(GeometryBatch batch, Vector2 foot)
    {
        batch.FillRectangle(new RectangleF(foot.X - 20f, foot.Y - 2f, 46f, 8f), new ColorRgba(0, 0, 0, 50));
        batch.FillRectangle(new RectangleF(foot.X - 22f, foot.Y - 18f, 44f, 18f), new ColorRgba(120, 126, 136));
        batch.FillRectangle(new RectangleF(foot.X - 24f, foot.Y - 24f, 48f, 8f), new ColorRgba(170, 176, 186));
        batch.FillCircle(new Vector2(foot.X, foot.Y - 34f), 18f, new ColorRgba(120, 200, 255, 50), 18);
        batch.FillCircle(new Vector2(foot.X, foot.Y - 34f), 8f, new ColorRgba(140, 220, 255), 14);
        batch.FillCircle(new Vector2(foot.X - 2f, foot.Y - 36f), 3f, new ColorRgba(235, 250, 255), 8);
    }

    private static void DrawBarrels(GeometryBatch batch, Vector2 foot)
    {
        for (int i = 0; i < 3; i++)
        {
            Vector2 p = foot + new Vector2((i - 1) * 14f, (i == 1) ? -4f : 0f);
            batch.FillCircle(p + new Vector2(3f, 3f), 7f, new ColorRgba(0, 0, 0, 45), 10);
            batch.FillRectangle(new RectangleF(p.X - 6f, p.Y - 16f, 12f, 16f), new ColorRgba(132, 88, 50));
            batch.FillRectangle(new RectangleF(p.X - 6f, p.Y - 13f, 12f, 2f), new ColorRgba(70, 70, 74));
            batch.FillRectangle(new RectangleF(p.X - 6f, p.Y - 5f, 12f, 2f), new ColorRgba(70, 70, 74));
            batch.FillCircle(new Vector2(p.X, p.Y - 16f), 6f, new ColorRgba(164, 116, 70), 10);
        }
    }

    private static ColorRgba Shade(ColorRgba c, float k) =>
        new((byte)Math.Clamp(c.R * k, 0f, 255f), (byte)Math.Clamp(c.G * k, 0f, 255f), (byte)Math.Clamp(c.B * k, 0f, 255f), c.A);
}
