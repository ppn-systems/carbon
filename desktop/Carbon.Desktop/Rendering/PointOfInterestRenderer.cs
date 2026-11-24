// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
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
    private readonly PointOfInterestResolver _landmarks;

    public PointOfInterestRenderer(float tileSize, PointOfInterestResolver landmarks)
    {
        _tileSize = tileSize;
        _landmarks = landmarks;
    }

    /// <summary>Draws the flat ground layer of a point of interest.</summary>
    public void DrawGround(GeometryBatch batch, in PointOfInterest poi)
    {
        LandmarkLayout layout = this.GetLayout(poi);
        Vector2 c = this.Centre(poi);
        float r = poi.Radius * _tileSize;

        switch (poi.Type)
        {
            case PointOfInterestType.RiverVillage:
                this.DrawFields(batch, layout);
                foreach (LandmarkPiece p in layout.Pieces)
                {
                    if (p.Kind == LandmarkPieceKind.House)
                    {
                        DrawPath(batch, c, this.ToWorld(p) + new Vector2(0f, 6f), 14f);
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

    /// <summary>Gets the layout (structures and fields) of a point of interest, shared with the engine.</summary>
    public LandmarkLayout GetLayout(in PointOfInterest poi) =>
        _landmarks.GetLayout(FloorDiv(poi.TileX, PointOfInterestResolver.SiteSize), FloorDiv(poi.TileY, PointOfInterestResolver.SiteSize));

    /// <summary>Ground contact point of a structure in world pixels.</summary>
    public Vector2 ToWorld(in LandmarkPiece piece) => new(piece.X * _tileSize, piece.Y * _tileSize);

    /// <summary>Draws one upright structure.</summary>
    public void DrawPiece(GeometryBatch batch, in LandmarkPiece piece)
    {
        Vector2 position = this.ToWorld(piece);
        float width = piece.Width * _tileSize;
        switch (piece.Kind)
        {
            case LandmarkPieceKind.House:
                DrawHouse(batch, position, width, piece.Variant, isWarehouse: false);
                break;
            case LandmarkPieceKind.Warehouse:
                DrawHouse(batch, position, width, piece.Variant, isWarehouse: true);
                break;
            case LandmarkPieceKind.Well:
                DrawWell(batch, position);
                break;
            case LandmarkPieceKind.Tent:
                DrawTent(batch, position, width, piece.Variant);
                break;
            case LandmarkPieceKind.Campfire:
                DrawCampfire(batch, position);
                break;
            case LandmarkPieceKind.ShrineGate:
                DrawShrineGate(batch, position, width);
                break;
            case LandmarkPieceKind.StoneLantern:
                DrawStoneLantern(batch, position);
                break;
            case LandmarkPieceKind.Altar:
                DrawAltar(batch, position);
                break;
            case LandmarkPieceKind.Barrel:
                DrawBarrels(batch, position);
                break;
            default:
                break;
        }
    }

    // ── Layout ───────────────────────────────────────────────────────────────

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

    private static Vector2 SeaDirection(in PointOfInterest poi)
    {
        Vector2 d = new(poi.FacingX, poi.FacingY);
        return d.LengthSquared() > 0.01f ? Vector2.Normalize(d) : new Vector2(0f, 1f);
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : ((a + 1) / b) - 1;

    private Vector2 Centre(in PointOfInterest poi) =>
        new((poi.TileX + 0.5f) * _tileSize, (poi.TileY + 0.5f) * _tileSize);

    private void DrawFields(GeometryBatch batch, LandmarkLayout layout)
    {
        foreach (LandmarkField field in layout.Fields)
        {
            Vector2 min = new(field.MinX * _tileSize, field.MinY * _tileSize);
            Vector2 max = new(field.MaxX * _tileSize, field.MaxY * _tileSize);
            byte crop = field.Crop;
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
