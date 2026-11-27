// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Maps.Landmarks;
using Carbon.Desktop.Rendering.Atmospherics;

namespace Carbon.Desktop.Rendering.World;

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
    public void DrawGround(GeometryBatch batch, in PointOfInterest poi, float time, Vector2 playerWorld)
    {
        LandmarkLayout layout = this.GetLayout(poi);
        Vector2 c = this.Centre(poi);
        float r = poi.Radius * _tileSize;

        switch (poi.Type)
        {
            case PointOfInterestType.RiverVillage:
            case PointOfInterestType.DesertVillage:
            case PointOfInterestType.SnowVillage:
            case PointOfInterestType.SwampVillage:
                this.DrawFields(batch, layout);

                // Village streets, side lanes and the spur to every front door (ADR-027).
                this.DrawPaths(batch, layout);

                // The square covers only the well area the planner keeps free, so it never paints over a building.
                DrawPlaza(batch, c, 3.3f * _tileSize);
                break;

            case PointOfInterestType.Harbor:
                DrawPlaza(batch, c, r * 0.35f);
                this.DrawPiers(batch, poi, c, r, time);
                break;

            case PointOfInterestType.LakesideCamp:
                batch.FillCircle(c, r * 0.55f, new ColorRgba(150, 128, 88, 150), 28);
                batch.FillCircle(c, r * 0.40f, new ColorRgba(170, 146, 100, 170), 28);
                break;

            case PointOfInterestType.MountainShrine:
                DrawShrinePlatform(batch, c, r * 0.62f);
                break;

            case PointOfInterestType.WorldTree:
                DrawWorldTreeGround(batch, c + new Vector2(0f, _tileSize), r, time);
                break;

            case PointOfInterestType.MineEntrance:
                DrawMineYard(batch, c, r);
                break;

            case PointOfInterestType.AncientRuins:
                DrawShrinePlatform(batch, c, r * 0.85f);
                break;

            case PointOfInterestType.BanditCamp:
                batch.FillCircle(c, r * 0.72f, new ColorRgba(110, 86, 62, 130), 24);
                break;

            case PointOfInterestType.Watchtower:
                batch.FillCircle(c, r * 0.65f, new ColorRgba(136, 128, 114, 150), 20);
                break;

            case PointOfInterestType.BossArena:
                DrawShrinePlatform(batch, c, r * 0.92f);
                batch.FillCircle(c, r * 0.48f, new ColorRgba(110, 44, 48, 120), 24);
                break;

            case PointOfInterestType.None:
            default:
                break;
        }

        // Interiors of the buildings the player has walked into (the building itself is drawn cut away).
        foreach (LandmarkPiece p in layout.Pieces)
        {
            if (this.IsInside(p, playerWorld))
            {
                DrawInterior(batch, this.ToWorld(p), p.Width * _tileSize, p.Depth * _tileSize, p.Look, p.Style, p.Kind == LandmarkPieceKind.Warehouse, time);
            }
        }
    }

    /// <summary>Adds the lights of a point of interest (windows, fires, lanterns, runes) to <paramref name="lights"/>.</summary>
    public void CollectLights(in PointOfInterest poi, List<PointLight> lights, float time)
    {
        ColorRgba warm = new(255, 186, 104);
        ColorRgba spirit = new(120, 255, 210);
        foreach (LandmarkPiece p in this.GetLayout(poi).Pieces)
        {
            Vector2 foot = this.ToWorld(p);
            float flicker = 0.85f + (0.15f * MathF.Sin((time * 9f) + foot.X));
            switch (p.Kind)
            {
                case LandmarkPieceKind.House:
                case LandmarkPieceKind.Warehouse:
                    {
                        float width = p.Width * _tileSize;
                        float wallTop = foot.Y - (p.Kind == LandmarkPieceKind.Warehouse ? 46f : 38f);
                        lights.Add(new PointLight(foot + new Vector2(0f, 10f), 70f, warm, 0.7f)); // light spilling from the door
                        if (p.Kind == LandmarkPieceKind.House)
                        {
                            for (int k = 0; k < 4; k++)
                            {
                                float side = k < 2 ? -1f : 1f;
                                float x = foot.X + (side * width * ((k % 2) == 0 ? 0.20f : 0.36f));
                                lights.Add(new PointLight(new Vector2(x, wallTop + 14f), 46f, warm, 0.8f * flicker));
                            }
                        }

                        break;
                    }

                case LandmarkPieceKind.Campfire:
                    lights.Add(new PointLight(foot + new Vector2(0f, -8f), 190f, new ColorRgba(255, 150, 70), flicker));
                    break;
                case LandmarkPieceKind.StoneLantern:
                    lights.Add(new PointLight(foot + new Vector2(0f, -30f), p.Variant == 1 ? 70f : 110f, p.Variant == 1 ? spirit : warm, p.Variant == 1 ? 0.45f : 0.8f));
                    break;
                case LandmarkPieceKind.Altar:
                    lights.Add(new PointLight(foot + new Vector2(0f, -36f), 130f, new ColorRgba(140, 210, 255), 0.9f));
                    break;
                case LandmarkPieceKind.WorldTree:
                    // One broad spirit glow around the base and a soft one in the crown; the mushroom ring
                    // reads as a single faint halo instead of many overlapping lights.
                    lights.Add(new PointLight(foot + new Vector2(0f, -50f), 240f, spirit, 0.55f));
                    lights.Add(new PointLight(foot + new Vector2(0f, -340f), 260f, new ColorRgba(170, 255, 210), 0.25f));
                    for (int i = 0; i < 14; i += 2)
                    {
                        float a = i * (MathF.Tau / 14f);
                        lights.Add(new PointLight(foot + new Vector2(MathF.Cos(a) * 170f, (MathF.Sin(a) * 74f) - 4f), 34f, spirit, 0.35f));
                    }

                    break;
                case LandmarkPieceKind.MineEntrance:
                    lights.Add(new PointLight(foot + new Vector2(0f, -14f), 90f, warm, 0.5f * flicker)); // lamps inside the adit
                    break;
                case LandmarkPieceKind.Watchtower:
                    lights.Add(new PointLight(foot + new Vector2(0f, -72f), 170f, warm, 0.85f * flicker));
                    break;
                case LandmarkPieceKind.RuinedPillar:
                    lights.Add(new PointLight(foot + new Vector2(0f, -20f), 85f, spirit, 0.35f));
                    break;
                case LandmarkPieceKind.Well:
                case LandmarkPieceKind.Tent:
                case LandmarkPieceKind.ShrineGate:
                case LandmarkPieceKind.Barrel:
                case LandmarkPieceKind.OreCart:
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Finds the building the player (world pixels) stands in, with its room rectangle in world pixels.
    /// </summary>
    public bool TryGetBuildingAt(Vector2 playerWorld, out LandmarkPiece building, out RectangleF room)
    {
        int tx = (int)MathF.Floor(playerWorld.X / _tileSize);
        int ty = (int)MathF.Floor(playerWorld.Y / _tileSize);
        foreach (LandmarkPiece p in _landmarks.GetLayout(FloorDiv(tx, PointOfInterestResolver.SiteSize), FloorDiv(ty, PointOfInterestResolver.SiteSize)).Pieces)
        {
            if (this.IsInside(p, playerWorld))
            {
                float lift = p.Style == LandmarkPiece.StyleSwamp ? 10f : 0f;
                float w = p.Width * _tileSize;
                float d = p.Depth * _tileSize;
                Vector2 foot = this.ToWorld(p);
                building = p;
                room = new RectangleF(foot.X - (w * 0.5f), foot.Y - lift - d, w, d);
                return true;
            }
        }

        building = default;
        room = default;
        return false;
    }

    /// <summary>Returns whether the player (world pixels) stands inside a building.</summary>
    public bool IsInside(in LandmarkPiece piece, Vector2 playerWorld) =>
        piece.IsInside(playerWorld.X / _tileSize, playerWorld.Y / _tileSize);

    /// <summary>Gets the layout (structures and fields) of a point of interest, shared with the engine.</summary>
    public LandmarkLayout GetLayout(in PointOfInterest poi) =>
        _landmarks.GetLayout(FloorDiv(poi.TileX, PointOfInterestResolver.SiteSize), FloorDiv(poi.TileY, PointOfInterestResolver.SiteSize));

    /// <summary>Ground contact point of a structure in world pixels.</summary>
    public Vector2 ToWorld(in LandmarkPiece piece) => new(piece.X * _tileSize, piece.Y * _tileSize);

    /// <summary>Draws one upright structure. Buildings the player stands in are drawn cut away.</summary>
    public void DrawPiece(GeometryBatch batch, in LandmarkPiece piece, float time, Vector2 playerWorld)
    {
        Vector2 position = this.ToWorld(piece);
        float width = piece.Width * _tileSize;
        switch (piece.Kind)
        {
            case LandmarkPieceKind.House:
            case LandmarkPieceKind.Warehouse:
                DrawHouse(
                    batch, position, width, piece.Depth * _tileSize, piece.Look, piece.Style,
                    piece.Kind == LandmarkPieceKind.Warehouse, this.IsInside(piece, playerWorld), time);
                break;
            case LandmarkPieceKind.Well:
                DrawWell(batch, position);
                break;
            case LandmarkPieceKind.Tent:
                DrawTent(batch, position, width, piece.Variant);
                break;
            case LandmarkPieceKind.Campfire:
                DrawCampfire(batch, position, time);
                break;
            case LandmarkPieceKind.ShrineGate:
                DrawShrineGate(batch, position, width);
                break;
            case LandmarkPieceKind.StoneLantern:
                if (piece.Variant == 1)
                {
                    DrawRuneStone(batch, position, time);
                }
                else
                {
                    DrawStoneLantern(batch, position, time);
                }

                break;
            case LandmarkPieceKind.WorldTree:
                DrawWorldTreeTrunk(batch, position, time);
                break;
            case LandmarkPieceKind.Altar:
                DrawAltar(batch, position, time);
                break;
            case LandmarkPieceKind.Barrel:
                DrawBarrels(batch, position);
                break;
            case LandmarkPieceKind.MineEntrance:
                DrawMineEntrance(batch, position, width, piece.Variant);
                break;
            case LandmarkPieceKind.OreCart:
                DrawOreCart(batch, position, piece.Variant);
                break;
            case LandmarkPieceKind.RuinedPillar:
                DrawRuinedPillar(batch, position, piece.Variant);
                break;
            case LandmarkPieceKind.Watchtower:
                DrawWatchtower(batch, position, width, piece.Variant);
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

    // Edges first, then the fill, so joints and crossings read as one continuous dirt path.
    private void DrawPaths(GeometryBatch batch, LandmarkLayout layout)
    {
        ReadOnlySpan<LandmarkPath> paths = layout.Paths;
        for (int pass = 0; pass < 2; pass++)
        {
            ColorRgba color = pass == 0 ? new ColorRgba(140, 112, 74) : new ColorRgba(184, 156, 108);
            float grow = pass == 0 ? 4f : 0f;
            foreach (LandmarkPath path in paths)
            {
                Vector2 from = new(path.X0 * _tileSize, path.Y0 * _tileSize);
                Vector2 to = new(path.X1 * _tileSize, path.Y1 * _tileSize);
                float width = (path.Width * _tileSize) + grow - 4f;
                batch.DrawLine(from, to, color, width);
                batch.FillCircle(from, width * 0.5f, color, 12);
                batch.FillCircle(to, width * 0.5f, color, 12);
            }
        }
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

    private void DrawPiers(GeometryBatch batch, in PointOfInterest poi, Vector2 c, float r, float time)
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
            DrawBoat(batch, boat, sea, (byte)(i + 1), time + (i * 1.7f));
        }
    }

    private static void DrawBoat(GeometryBatch batch, Vector2 c, Vector2 forward, byte variant, float time)
    {
        // Gentle bobbing on the swell
        c += new Vector2(0f, MathF.Sin(time * 1.6f) * 1.6f);
        float sway = MathF.Sin(time * 1.1f) * 3f;
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
        batch.FillTriangle(c + new Vector2(0f, -46f), c + new Vector2(0f, -6f), c + new Vector2(22f + sway, -10f + (sway * 0.3f)), sail);
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

    private static readonly ColorRgba s_timber = new(102, 66, 40);

    // Draws a building. When `open` is set (the player is inside), only the low stubs of the walls are drawn
    // so the interior — drawn on the ground pass — stays visible: the roof is lifted off.
    private static void DrawHouse(
        GeometryBatch batch, Vector2 foot, float width, float depth, byte look, byte style, bool isWarehouse, bool open, float time)
    {
        float lift = style == LandmarkPiece.StyleSwamp ? 10f : 0f; // stilts
        float wallH = isWarehouse ? 46f : 38f;
        float x0 = foot.X - (width * 0.5f);
        float baseY = foot.Y - lift;
        float top = baseY - wallH;
        ColorRgba wall = WallColor(style, look, isWarehouse);

        if (open)
        {
            // Cut-away: low wall stubs around the room, door gap in the front wall.
            ColorRgba stub = Shade(wall, 0.8f);
            float door = (isWarehouse ? 48f : 32f) * 0.5f;
            batch.FillRectangle(new RectangleF(x0, baseY - 8f, (width * 0.5f) - door, 8f), stub);
            batch.FillRectangle(new RectangleF(foot.X + door, baseY - 8f, (width * 0.5f) - door, 8f), stub);
            batch.FillRectangle(new RectangleF(x0, baseY - 8f, width, 2f), Shade(wall, 1.1f));
            batch.FillRectangle(new RectangleF(x0, baseY - depth, 5f, depth - 8f), stub);
            batch.FillRectangle(new RectangleF(x0 + width - 5f, baseY - depth, 5f, depth - 8f), stub);
            batch.FillRectangle(new RectangleF(foot.X - door - 2f, baseY - 14f, 3f, 14f), s_timber);
            batch.FillRectangle(new RectangleF(foot.X + door - 1f, baseY - 14f, 3f, 14f), s_timber);
            return;
        }

        // Drop shadow to the south-east, matching the trees.
        batch.FillRectangle(new RectangleF(x0 + 8f, foot.Y - 6f, width + 6f, 14f), new ColorRgba(0, 0, 0, 55));

        if (lift > 0f)
        {
            // Stilts and a short ladder up to the door
            for (int i = 0; i < 4; i++)
            {
                float px = x0 + 4f + (i * ((width - 12f) / 3f));
                batch.FillRectangle(new RectangleF(px, baseY, 4f, lift + 2f), new ColorRgba(70, 52, 34));
            }

            batch.FillRectangle(new RectangleF(foot.X - 7f, baseY, 2f, lift + 2f), s_timber);
            batch.FillRectangle(new RectangleF(foot.X + 5f, baseY, 2f, lift + 2f), s_timber);
            for (float ly = baseY + 3f; ly < foot.Y; ly += 4f)
            {
                batch.FillRectangle(new RectangleF(foot.X - 7f, ly, 14f, 1.5f), s_timber);
            }
        }

        // Front wall
        batch.FillRectangle(new RectangleF(x0, top, width, wallH), wall);
        switch (style)
        {
            case LandmarkPiece.StyleSnow:
                for (float ly = top + 4f; ly < baseY; ly += 5f)
                {
                    batch.FillRectangle(new RectangleF(x0, ly, width, 1.5f), Shade(wall, 0.72f));
                }

                batch.FillRectangle(new RectangleF(x0 - 2f, top, 5f, wallH), Shade(wall, 0.85f));
                batch.FillRectangle(new RectangleF(x0 + width - 3f, top, 5f, wallH), Shade(wall, 0.85f));
                break;
            case LandmarkPiece.StyleSwamp:
                for (float lx = x0 + 6f; lx < x0 + width; lx += 7f)
                {
                    batch.FillRectangle(new RectangleF(lx, top, 1.5f, wallH), Shade(wall, 0.7f));
                }

                break;
            case LandmarkPiece.StyleDesert:
                // Smooth adobe with protruding roof beams (vigas)
                for (float lx = x0 + 6f; lx < x0 + width - 4f; lx += 12f)
                {
                    batch.FillRectangle(new RectangleF(lx, top + 2f, 4f, 3f), new ColorRgba(110, 74, 44));
                }

                break;
            default:
                batch.FillRectangle(new RectangleF(x0, baseY - 4f, width, 4f), new ColorRgba(120, 104, 88)); // stone footing
                batch.FillRectangle(new RectangleF(x0, top, 4f, wallH), s_timber);
                batch.FillRectangle(new RectangleF(x0 + width - 4f, top, 4f, wallH), s_timber);
                batch.FillRectangle(new RectangleF(x0, top, width, 3f), s_timber);
                break;
        }

        // Door and windows
        if (isWarehouse)
        {
            batch.FillRectangle(new RectangleF(foot.X - 22f, baseY - 36f, 44f, 36f), new ColorRgba(84, 52, 30));
            batch.DrawLine(new Vector2(foot.X, baseY - 36f), new Vector2(foot.X, baseY), new ColorRgba(60, 36, 20), 2f);
        }
        else
        {
            ColorRgba doorColor = style == LandmarkPiece.StyleDesert ? new ColorRgba(176, 70, 52) : new ColorRgba(92, 58, 34);
            batch.FillRectangle(new RectangleF(foot.X - 13f, baseY - 30f, 26f, 30f), s_timber);
            batch.FillRectangle(new RectangleF(foot.X - 11f, baseY - 28f, 22f, 28f), doorColor);
            batch.FillRectangle(new RectangleF(foot.X + 6f, baseY - 15f, 2.5f, 2.5f), new ColorRgba(230, 190, 90));

            // Warm light flickers gently behind the glass
            float flicker = 0.85f + (0.15f * MathF.Sin((time * 3.1f) + foot.X));
            ColorRgba glass = (look & 1) == 0
                ? new ColorRgba((byte)(255 * flicker), (byte)(214 * flicker), (byte)(120 * flicker))
                : new ColorRgba(150, 196, 220);
            // Two windows on each side of the door
            for (int k = 0; k < 4; k++)
            {
                float side = k < 2 ? -1f : 1f;
                float wx = foot.X + (side * width * ((k % 2) == 0 ? 0.20f : 0.36f)) - 6f;
                if (style == LandmarkPiece.StyleDesert)
                {
                    batch.FillRectangle(new RectangleF(wx + 1f, top + 9f, 10f, 9f), new ColorRgba(70, 46, 30));
                    batch.FillRectangle(new RectangleF(wx + 3f, top + 11f, 6f, 7f), glass);
                    continue;
                }

                batch.FillRectangle(new RectangleF(wx - 1f, top + 7f, 14f, 12f), s_timber);
                batch.FillRectangle(new RectangleF(wx + 1f, top + 9f, 10f, 8f), glass);
                batch.FillRectangle(new RectangleF(wx + 5.5f, top + 9f, 1f, 8f), s_timber);
            }
        }

        // Roof, seen from above over the whole depth of the building
        float overhang = style == LandmarkPiece.StyleDesert ? 2f : 6f;
        float rx = x0 - overhang;
        float rw = width + (2f * overhang);
        float roofTop = top - depth;
        if (style == LandmarkPiece.StyleDesert)
        {
            // Flat roof with a raised parapet
            batch.FillRectangle(new RectangleF(rx, roofTop, rw, depth + 2f), new ColorRgba(236, 206, 160));
            batch.FillRectangle(new RectangleF(rx + 4f, roofTop + 4f, rw - 8f, depth - 6f), new ColorRgba(210, 172, 122));
            batch.FillRectangle(new RectangleF(rx, top, rw, 2f), new ColorRgba(170, 132, 90));
            batch.FillCircle(new Vector2(rx + rw - 14f, roofTop + 12f), 5f, new ColorRgba(176, 96, 60), 10); // clay pot
            return;
        }

        ColorRgba roof = style switch
        {
            LandmarkPiece.StyleSnow => new ColorRgba(240, 246, 250),
            LandmarkPiece.StyleSwamp => new ColorRgba(128, 116, 66),
            _ => s_roofs[look % s_roofs.Length],
        };
        float front = depth * 0.58f;
        float back = depth * 0.42f;
        batch.FillRectangle(new RectangleF(rx, roofTop, rw, back), style == LandmarkPiece.StyleSnow ? new ColorRgba(206, 220, 234) : Shade(roof, 0.72f));
        batch.FillRectangle(new RectangleF(rx, top - front, rw, front + 4f), roof);
        if (style != LandmarkPiece.StyleSnow)
        {
            for (float y = top - front + 6f; y < top + 2f; y += 6f)
            {
                batch.FillRectangle(new RectangleF(rx, y, rw, 1.5f), Shade(roof, 0.82f));
            }
        }

        batch.FillRectangle(new RectangleF(rx, top - front - 2f, rw, 4f), Shade(roof, 1.1f));
        batch.FillRectangle(new RectangleF(rx, top + 2f, rw, 2f), Shade(roof, 0.55f));

        if (style == LandmarkPiece.StyleSnow)
        {
            // Icicles along the eave
            for (float ix = rx + 4f; ix < rx + rw - 2f; ix += 7f)
            {
                float len = 4f + (ix * 0.37f % 5f);
                batch.FillTriangle(new Vector2(ix, top + 4f), new Vector2(ix + 3f, top + 4f), new Vector2(ix + 1.5f, top + 4f + len), new ColorRgba(200, 230, 250));
            }
        }
        else if (style == LandmarkPiece.StyleSwamp)
        {
            // Ragged thatch fringe
            for (float ix = rx; ix < rx + rw - 3f; ix += 5f)
            {
                batch.FillTriangle(new Vector2(ix, top + 3f), new Vector2(ix + 5f, top + 3f), new Vector2(ix + 2.5f, top + 8f), Shade(roof, 0.8f));
            }
        }

        // Chimney with rising smoke (always on cabins, half of the other houses)
        if (!isWarehouse && (style == LandmarkPiece.StyleSnow || (look % 2) == 0))
        {
            float cx = x0 + (width * 0.72f);
            batch.FillRectangle(new RectangleF(cx, roofTop + 4f, 9f, 16f), new ColorRgba(120, 96, 84));
            batch.FillRectangle(new RectangleF(cx - 1f, roofTop + 2f, 11f, 3f), new ColorRgba(90, 72, 64));
            Vector2 mouth = new(cx + 4.5f, roofTop + 2f);
            float offset = (foot.X * 0.013f) + (foot.Y * 0.007f);
            for (int i = 0; i < 4; i++)
            {
                float phase = AmbientAnimator.Frac((time * 0.32f) + (i * 0.25f) + offset);
                Vector2 p = mouth + new Vector2((phase * 14f) + (MathF.Sin((time * 1.3f) + i + offset) * 2.5f), -phase * 34f);
                batch.FillCircle(p, 3.5f + (phase * 6f), new ColorRgba(228, 228, 228, (byte)(150f * (1f - phase))), 12);
            }
        }
    }

    private static ColorRgba WallColor(byte style, byte look, bool isWarehouse) => style switch
    {
        LandmarkPiece.StyleDesert => new ColorRgba(224, 190, 142),
        LandmarkPiece.StyleSnow => new ColorRgba(132, 90, 56),
        LandmarkPiece.StyleSwamp => new ColorRgba(96, 74, 50),
        _ => isWarehouse ? new ColorRgba(150, 106, 66) : s_walls[look / 3 % s_walls.Length],
    };

    // Interior of a building (floor, back wall, furniture), drawn on the ground pass while the player is inside.
    private static void DrawInterior(GeometryBatch batch, Vector2 foot, float width, float depth, byte look, byte style, bool isWarehouse, float time)
    {
        float lift = style == LandmarkPiece.StyleSwamp ? 10f : 0f;
        float x0 = foot.X - (width * 0.5f);
        float baseY = foot.Y - lift;
        float y0 = baseY - depth;
        ColorRgba floor = style switch
        {
            LandmarkPiece.StyleDesert => new ColorRgba(204, 164, 112),
            LandmarkPiece.StyleSwamp => new ColorRgba(110, 84, 56),
            LandmarkPiece.StyleSnow => new ColorRgba(150, 104, 64),
            _ => new ColorRgba(168, 118, 72),
        };

        // Floor: planks (or clay tiles in the desert)
        batch.FillRectangle(new RectangleF(x0, y0, width, depth), floor);
        if (style == LandmarkPiece.StyleDesert)
        {
            for (float ty = y0 + 12f; ty < baseY; ty += 12f)
            {
                batch.FillRectangle(new RectangleF(x0, ty, width, 1f), Shade(floor, 0.85f));
            }

            for (float tx = x0 + 12f; tx < x0 + width; tx += 12f)
            {
                batch.FillRectangle(new RectangleF(tx, y0, 1f, depth), Shade(floor, 0.85f));
            }
        }
        else
        {
            int row = 0;
            for (float ty = y0 + 8f; ty < baseY; ty += 8f, row++)
            {
                batch.FillRectangle(new RectangleF(x0, ty, width, 1f), Shade(floor, 0.78f));
                for (float tx = x0 + (row % 2 * 20f) + 10f; tx < x0 + width; tx += 40f)
                {
                    batch.FillRectangle(new RectangleF(tx, ty - 8f, 1f, 8f), Shade(floor, 0.85f));
                }
            }
        }

        // Inner face of the back wall with a shelf, and a soft shadow at its foot
        ColorRgba wall = WallColor(style, look, isWarehouse);
        batch.FillRectangle(new RectangleF(x0, y0, width, 22f), Shade(wall, 0.85f));
        batch.FillRectangle(new RectangleF(x0, y0 + 22f, width, 3f), Shade(wall, 0.55f));
        batch.FillRectangle(new RectangleF(x0, y0 + 25f, width, 6f), new ColorRgba(0, 0, 0, 35));

        // Daylight falling through the front windows onto the floor
        for (int k = 0; k < 4; k++)
        {
            float side = k < 2 ? -1f : 1f;
            float wx = foot.X + (side * width * ((k % 2) == 0 ? 0.20f : 0.36f));
            batch.FillRectangle(new RectangleF(wx - 7f, baseY - 26f, 14f, 18f), new ColorRgba(255, 240, 190, 40));
        }

        if (isWarehouse)
        {
            // Stacked crates along the walls, sacks and barrels on the floor
            for (int i = 0; i < 6; i++)
            {
                float cx = x0 + 10f + (i * ((width - 36f) / 5f));
                DrawCrate(batch, new Vector2(cx, y0 + 26f), 24f);
                if ((i % 2) == 0)
                {
                    DrawCrate(batch, new Vector2(cx + 4f, y0 + 14f), 20f);
                }
            }

            for (int i = 0; i < 4; i++)
            {
                batch.FillCircle(new Vector2(x0 + 22f + (i * 14f), baseY - 22f), 8f, new ColorRgba(196, 176, 130), 12);
            }

            DrawBarrels(batch, new Vector2(x0 + width - 40f, baseY - 14f));
            return;
        }

        // Shelf with pots on the back wall
        batch.FillRectangle(new RectangleF(foot.X - 30f, y0 + 6f, 60f, 3f), s_timber);
        for (int i = 0; i < 4; i++)
        {
            ColorRgba pot = (i % 2) == 0 ? new ColorRgba(176, 96, 60) : new ColorRgba(120, 150, 170);
            batch.FillRectangle(new RectangleF(foot.X - 26f + (i * 15f), y0 - 1f, 8f, 7f), pot);
        }

        // Big rug in the middle
        ColorRgba rug = style == LandmarkPiece.StyleSnow ? new ColorRgba(236, 230, 220) : new ColorRgba(150, 54, 48);
        float rugW = width * 0.42f;
        float rugH = depth * 0.42f;
        float rugY = y0 + (depth * 0.42f);
        batch.FillRectangle(new RectangleF(foot.X - (rugW * 0.5f), rugY, rugW, rugH), rug);
        batch.DrawRectangle(new RectangleF(foot.X - (rugW * 0.5f) + 4f, rugY + 4f, rugW - 8f, rugH - 8f), Shade(rug, 0.75f), 2f);
        batch.FillRectangle(new RectangleF(foot.X - 6f, rugY + (rugH * 0.5f) - 4f, 12f, 8f), Shade(rug, 1.25f));

        // Bed in the back-left corner
        float bx = x0 + 10f;
        float by = y0 + 30f;
        ColorRgba blanket = (look & 1) == 0 ? new ColorRgba(70, 110, 170) : new ColorRgba(150, 70, 70);
        batch.FillRectangle(new RectangleF(bx + 3f, by + 3f, 30f, 44f), new ColorRgba(0, 0, 0, 40));
        batch.FillRectangle(new RectangleF(bx, by, 30f, 44f), new ColorRgba(110, 74, 44));
        batch.FillRectangle(new RectangleF(bx + 2f, by + 13f, 26f, 30f), blanket);
        batch.FillRectangle(new RectangleF(bx + 2f, by + 13f, 26f, 4f), Shade(blanket, 1.2f));
        batch.FillRectangle(new RectangleF(bx + 4f, by + 3f, 22f, 9f), new ColorRgba(240, 236, 226));

        // Chest at the foot of the bed
        batch.FillRectangle(new RectangleF(bx + 2f, by + 50f, 26f, 14f), new ColorRgba(130, 84, 44));
        batch.FillRectangle(new RectangleF(bx + 2f, by + 55f, 26f, 2f), new ColorRgba(214, 170, 60));

        // Table with stools, a candle and dishes, right of centre
        float tx0 = foot.X + (width * 0.10f);
        float ty0 = y0 + (depth * 0.30f);
        batch.FillRectangle(new RectangleF(tx0 + 3f, ty0 + 3f, 34f, 20f), new ColorRgba(0, 0, 0, 40));
        batch.FillRectangle(new RectangleF(tx0, ty0, 34f, 20f), new ColorRgba(130, 88, 52));
        batch.FillRectangle(new RectangleF(tx0, ty0, 34f, 3f), new ColorRgba(156, 108, 66));
        batch.FillCircle(new Vector2(tx0 - 7f, ty0 + 10f), 5f, new ColorRgba(110, 74, 44), 10);
        batch.FillCircle(new Vector2(tx0 + 41f, ty0 + 10f), 5f, new ColorRgba(110, 74, 44), 10);
        batch.FillCircle(new Vector2(tx0 + 10f, ty0 + 10f), 4f, new ColorRgba(230, 220, 200), 10);
        batch.FillCircle(new Vector2(tx0 + 24f, ty0 + 10f), 4f, new ColorRgba(230, 220, 200), 10);
        float candle = 0.5f + (0.5f * MathF.Sin(time * 7f));
        batch.FillRectangle(new RectangleF(tx0 + 16f, ty0 + 2f, 2f, 5f), new ColorRgba(245, 240, 225));
        batch.FillCircle(new Vector2(tx0 + 17f, ty0 + 1f), 1.8f + candle, new ColorRgba(255, 200, 90), 8);
        batch.FillCircle(new Vector2(tx0 + 17f, ty0 + 2f), 14f + (candle * 3f), new ColorRgba(255, 200, 100, 26), 14);

        // Potted plant in the front-left corner
        batch.FillRectangle(new RectangleF(x0 + 10f, baseY - 20f, 10f, 9f), new ColorRgba(176, 96, 60));
        batch.FillCircle(new Vector2(x0 + 15f, baseY - 24f), 7f, new ColorRgba(64, 140, 70), 12);

        // Hearth in the back-right corner with a live fire (desert homes keep water jars instead)
        float hx = x0 + width - 40f;
        if (style == LandmarkPiece.StyleDesert)
        {
            batch.FillCircle(new Vector2(hx + 12f, y0 + 34f), 9f, new ColorRgba(176, 96, 60), 12);
            batch.FillCircle(new Vector2(hx + 26f, y0 + 38f), 7f, new ColorRgba(160, 86, 54), 12);
            batch.FillCircle(new Vector2(hx + 12f, y0 + 31f), 4f, new ColorRgba(60, 100, 140), 10);
            return;
        }

        batch.FillRectangle(new RectangleF(hx, y0 + 2f, 32f, 28f), new ColorRgba(120, 116, 112));
        batch.FillRectangle(new RectangleF(hx - 3f, y0, 38f, 5f), new ColorRgba(150, 146, 140));
        batch.FillRectangle(new RectangleF(hx + 6f, y0 + 12f, 20f, 18f), new ColorRgba(40, 30, 26));
        float flame = MathF.Sin(time * 10f) * 2f;
        batch.FillTriangle(new Vector2(hx + 8f, y0 + 30f), new Vector2(hx + 24f, y0 + 30f), new Vector2(hx + 16f + flame, y0 + 14f), new ColorRgba(249, 115, 22));
        batch.FillTriangle(new Vector2(hx + 11f, y0 + 30f), new Vector2(hx + 21f, y0 + 30f), new Vector2(hx + 16f - flame, y0 + 19f), new ColorRgba(251, 191, 36));
        batch.FillCircle(new Vector2(hx + 16f, y0 + 34f), 30f, new ColorRgba(255, 170, 80, (byte)(30 + (flame * 6f))), 18);
        DrawCrate(batch, new Vector2(hx - 18f, y0 + 26f), 14f); // firewood box
    }

    private static void DrawCrate(GeometryBatch batch, Vector2 topLeft, float size)
    {
        batch.FillRectangle(new RectangleF(topLeft.X + 2f, topLeft.Y + 2f, size, size), new ColorRgba(0, 0, 0, 40));
        batch.FillRectangle(new RectangleF(topLeft.X, topLeft.Y, size, size), new ColorRgba(150, 104, 60));
        batch.DrawRectangle(new RectangleF(topLeft.X, topLeft.Y, size, size), new ColorRgba(100, 66, 36), 2f);
        batch.DrawLine(topLeft, topLeft + new Vector2(size, size), new ColorRgba(100, 66, 36), 2f);
    }

    // ── World Tree ───────────────────────────────────────────────────────────

    /// <summary>Draws the World Tree crown above everything else (see <see cref="WorldTreeArt"/>).</summary>
    public void DrawWorldTreeCanopy(GeometryBatch batch, in PointOfInterest poi, float time, Vector2 playerWorld)
    {
        foreach (LandmarkPiece p in this.GetLayout(poi).Pieces)
        {
            if (p.Kind == LandmarkPieceKind.WorldTree)
            {
                WorldTreeArt.DrawCanopy(batch, this.ToWorld(p), time, playerWorld);
            }
        }
    }

    private static void DrawWorldTreeGround(GeometryBatch batch, Vector2 foot, float radius, float time) =>
        WorldTreeArt.DrawGround(batch, foot, radius, time);

    private static void DrawWorldTreeTrunk(GeometryBatch batch, Vector2 foot, float time) =>
        WorldTreeArt.DrawTrunk(batch, foot, time);

    private static void DrawRuneStone(GeometryBatch batch, Vector2 foot, float time)
    {
        float glow = 0.5f + (0.5f * MathF.Sin((time * 1.8f) + foot.X));
        batch.FillRectangle(new RectangleF(foot.X - 8f, foot.Y - 3f, 22f, 7f), new ColorRgba(0, 0, 0, 55));
        Span<Vector2> stone =
        [
            foot + new Vector2(-10f, 0f),
            foot + new Vector2(10f, 0f),
            foot + new Vector2(8f, -34f),
            foot + new Vector2(0f, -42f),
            foot + new Vector2(-9f, -32f),
        ];
        batch.FillConvexPolygon(stone, new ColorRgba(110, 116, 126));
        batch.FillRectangle(new RectangleF(foot.X - 10f, foot.Y - 34f, 4f, 34f), new ColorRgba(140, 146, 156));
        // Rune: a diamond with a dot, glowing
        ColorRgba rune = new(120, 255, 200, (byte)(120 + (glow * 120f)));
        Vector2 rc = foot + new Vector2(0f, -22f);
        batch.DrawLine(rc + new Vector2(0f, -8f), rc + new Vector2(5f, 0f), rune, 2f);
        batch.DrawLine(rc + new Vector2(5f, 0f), rc + new Vector2(0f, 8f), rune, 2f);
        batch.DrawLine(rc + new Vector2(0f, 8f), rc + new Vector2(-5f, 0f), rune, 2f);
        batch.DrawLine(rc + new Vector2(-5f, 0f), rc + new Vector2(0f, -8f), rune, 2f);
        batch.FillCircle(rc, 1.8f, rune, 6);
        batch.FillCircle(foot + new Vector2(0f, -22f), 14f, new ColorRgba(120, 255, 200, (byte)(20 + (glow * 30f))), 12);
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

    private static void DrawCampfire(GeometryBatch batch, Vector2 foot, float time)
    {
        float flicker = (MathF.Sin(time * 11f) * 0.5f) + (MathF.Sin(time * 17.3f) * 0.3f);
        batch.FillCircle(foot, 34f + (flicker * 4f), new ColorRgba(255, 160, 60, (byte)(40 + (flicker * 12f))), 24); // glow
        for (int i = 0; i < 8; i++)
        {
            float a = i * (MathF.Tau / 8f);
            batch.FillCircle(foot + new Vector2(MathF.Cos(a) * 12f, MathF.Sin(a) * 9f), 4f, new ColorRgba(120, 124, 130), 8);
        }

        batch.DrawLine(foot + new Vector2(-10f, 4f), foot + new Vector2(10f, -4f), new ColorRgba(92, 58, 32), 5f);
        batch.DrawLine(foot + new Vector2(-10f, -4f), foot + new Vector2(10f, 4f), new ColorRgba(110, 70, 40), 5f);
        // Dancing flames
        float lean = MathF.Sin(time * 7.1f) * 3f;
        batch.FillTriangle(foot + new Vector2(-8f, 0f), foot + new Vector2(8f, 0f), foot + new Vector2(lean, -22f - (flicker * 5f)), new ColorRgba(234, 88, 12));
        batch.FillTriangle(foot + new Vector2(-7f, 0f), foot + new Vector2(1f, 0f), foot + new Vector2(-4f + (lean * 0.6f), -14f + (flicker * 3f)), new ColorRgba(249, 115, 22));
        batch.FillTriangle(foot + new Vector2(-5f, 0f), foot + new Vector2(5f, 0f), foot + new Vector2(1f - (lean * 0.5f), -15f - (flicker * 3f)), new ColorRgba(251, 191, 36));

        // Sparks drifting up
        for (int i = 0; i < 4; i++)
        {
            float phase = AmbientAnimator.Frac((time * 0.8f) + (i * 0.27f));
            Vector2 p = foot + new Vector2((MathF.Sin((time * 3f) + (i * 2f)) * 6f) + ((i - 1.5f) * 3f), -14f - (phase * 30f));
            batch.FillRectangle(new RectangleF(p.X, p.Y, 2f, 2f), new ColorRgba(253, 186, 116, (byte)(255f * (1f - phase))));
        }

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

    private static void DrawStoneLantern(GeometryBatch batch, Vector2 foot, float time)
    {
        ColorRgba stone = new(150, 156, 166);
        ColorRgba stoneDark = new(110, 116, 126);
        batch.FillRectangle(new RectangleF(foot.X - 6f, foot.Y - 2f, 18f, 6f), new ColorRgba(0, 0, 0, 50));
        batch.FillRectangle(new RectangleF(foot.X - 8f, foot.Y - 6f, 16f, 6f), stoneDark);
        batch.FillRectangle(new RectangleF(foot.X - 3f, foot.Y - 24f, 6f, 18f), stone);
        batch.FillRectangle(new RectangleF(foot.X - 8f, foot.Y - 36f, 16f, 12f), stoneDark);
        batch.FillRectangle(new RectangleF(foot.X - 4f, foot.Y - 33f, 8f, 6f), new ColorRgba(255, 200, 90));
        float glow = 0.5f + (0.5f * MathF.Sin((time * 2.3f) + foot.X));
        batch.FillCircle(new Vector2(foot.X, foot.Y - 30f), 13f + (glow * 4f), new ColorRgba(255, 190, 80, (byte)(35 + (glow * 30f))), 14);
        batch.FillTriangle(new Vector2(foot.X - 12f, foot.Y - 36f), new Vector2(foot.X + 12f, foot.Y - 36f), new Vector2(foot.X, foot.Y - 46f), stone);
    }

    private static void DrawAltar(GeometryBatch batch, Vector2 foot, float time)
    {
        float bob = MathF.Sin(time * 1.4f) * 3f;
        float pulse = 0.5f + (0.5f * MathF.Sin(time * 2.1f));
        batch.FillRectangle(new RectangleF(foot.X - 20f, foot.Y - 2f, 46f, 8f), new ColorRgba(0, 0, 0, 50));
        batch.FillRectangle(new RectangleF(foot.X - 22f, foot.Y - 18f, 44f, 18f), new ColorRgba(120, 126, 136));
        batch.FillRectangle(new RectangleF(foot.X - 24f, foot.Y - 24f, 48f, 8f), new ColorRgba(170, 176, 186));
        Vector2 orb = new(foot.X, foot.Y - 36f + bob);
        batch.FillCircle(orb, 16f + (pulse * 6f), new ColorRgba(120, 200, 255, (byte)(35 + (pulse * 35f))), 18);
        batch.FillCircle(orb, 8f, new ColorRgba(140, 220, 255), 14);
        batch.FillCircle(orb + new Vector2(-2f, -2f), 3f, new ColorRgba(235, 250, 255), 8);

        // Motes orbiting the orb
        for (int i = 0; i < 3; i++)
        {
            float a = (time * 1.5f) + (i * (MathF.Tau / 3f));
            batch.FillRectangle(new RectangleF(orb.X + (MathF.Cos(a) * 14f) - 1f, orb.Y + (MathF.Sin(a) * 6f) - 1f, 2f, 2f), new ColorRgba(200, 240, 255, 200));
        }
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

    // Trodden yard in front of the mine: packed earth, spoil heaps of grey rock and a short rail line.
    private static void DrawMineYard(GeometryBatch batch, Vector2 c, float r)
    {
        batch.FillCircle(c + new Vector2(0f, r * 0.15f), r * 0.62f, new ColorRgba(126, 104, 78, 150), 28);
        batch.FillCircle(c + new Vector2(0f, r * 0.15f), r * 0.46f, new ColorRgba(146, 122, 92, 170), 28);
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Vector2 heap = c + new Vector2(side * r * 0.52f, -r * 0.05f);
            batch.FillCircle(heap + new Vector2(3f, 4f), 22f, new ColorRgba(0, 0, 0, 40), 14);
            batch.FillCircle(heap, 20f, new ColorRgba(112, 110, 108), 14);
            batch.FillCircle(heap + new Vector2(-6f, -6f), 12f, new ColorRgba(140, 138, 134), 12);
            batch.FillCircle(heap + new Vector2(8f, -2f), 7f, new ColorRgba(96, 94, 92), 10);
        }

        // Rails from the portal out into the yard
        Vector2 a = c + new Vector2(0f, -r * 0.05f);
        Vector2 b = c + new Vector2(0f, r * 0.55f);
        for (float y = a.Y; y < b.Y; y += 8f)
        {
            batch.FillRectangle(new RectangleF(c.X - 12f, y, 24f, 3f), new ColorRgba(98, 70, 44));
        }

        batch.DrawLine(new Vector2(c.X - 8f, a.Y), new Vector2(c.X - 8f, b.Y), new ColorRgba(80, 84, 92), 2f);
        batch.DrawLine(new Vector2(c.X + 8f, a.Y), new Vector2(c.X + 8f, b.Y), new ColorRgba(80, 84, 92), 2f);
    }

    // A timbered adit cut into a rock face: dark opening, two posts and a lintel, a lantern hook.
    private static void DrawMineEntrance(GeometryBatch batch, Vector2 foot, float width, byte variant)
    {
        ColorRgba rock = variant switch
        {
            1 => new ColorRgba(150, 104, 84),  // red sandstone
            2 => new ColorRgba(78, 74, 78),    // basalt
            3 => new ColorRgba(176, 184, 196), // frosted granite
            _ => new ColorRgba(128, 124, 118),
        };
        float hw = width * 0.5f;

        // Rock face: a lumpy mound of overlapping boulders
        batch.FillCircle(foot + new Vector2(4f, 4f), hw * 0.95f, new ColorRgba(0, 0, 0, 50), 24);
        for (int i = 0; i < 7; i++)
        {
            float t = (i / 6f) - 0.5f;
            float rr = hw * (0.42f - (MathF.Abs(t) * 0.25f));
            Vector2 p = foot + new Vector2(t * hw * 1.6f, -hw * (0.55f - (MathF.Abs(t) * 0.5f)));
            batch.FillCircle(p, rr, Shade(rock, 0.85f + (0.05f * (i % 3))), 18);
        }

        batch.FillCircle(foot + new Vector2(-hw * 0.25f, -hw * 0.85f), hw * 0.22f, Shade(rock, 1.15f), 14);

        // Opening
        float ow = 34f;
        float oh = 40f;
        batch.FillRectangle(new RectangleF(foot.X - (ow * 0.5f), foot.Y - oh, ow, oh), new ColorRgba(22, 18, 16));
        batch.FillCircle(new Vector2(foot.X, foot.Y - oh), ow * 0.5f, new ColorRgba(22, 18, 16), 16);

        // Timber frame
        ColorRgba wood = new(118, 78, 44);
        batch.FillRectangle(new RectangleF(foot.X - (ow * 0.5f) - 6f, foot.Y - oh - 4f, 6f, oh + 4f), wood);
        batch.FillRectangle(new RectangleF(foot.X + (ow * 0.5f), foot.Y - oh - 4f, 6f, oh + 4f), wood);
        batch.FillRectangle(new RectangleF(foot.X - (ow * 0.5f) - 10f, foot.Y - oh - 10f, ow + 20f, 7f), Shade(wood, 1.1f));
        batch.FillRectangle(new RectangleF(foot.X - (ow * 0.5f) - 10f, foot.Y - oh - 4f, ow + 20f, 1.5f), Shade(wood, 0.7f));

        // Lantern on the lintel
        batch.DrawLine(new Vector2(foot.X + 12f, foot.Y - oh - 3f), new Vector2(foot.X + 12f, foot.Y - oh + 5f), new ColorRgba(40, 40, 44), 1f);
        batch.FillRectangle(new RectangleF(foot.X + 9f, foot.Y - oh + 5f, 6f, 7f), new ColorRgba(255, 196, 110));
    }

    // An ore cart on rails, loaded with ore of the mine's colour.
    private static void DrawOreCart(GeometryBatch batch, Vector2 foot, byte variant)
    {
        ColorRgba ore = variant switch
        {
            1 => new ColorRgba(214, 176, 60), // gold
            2 => new ColorRgba(70, 190, 200), // copper patina
            _ => new ColorRgba(150, 92, 70),  // iron
        };
        batch.FillCircle(foot + new Vector2(3f, 2f), 18f, new ColorRgba(0, 0, 0, 45), 14);
        batch.FillCircle(foot + new Vector2(-9f, -3f), 4f, new ColorRgba(50, 50, 56), 10);
        batch.FillCircle(foot + new Vector2(9f, -3f), 4f, new ColorRgba(50, 50, 56), 10);
        Span<Vector2> body =
        [
            foot + new Vector2(-17f, -20f),
            foot + new Vector2(17f, -20f),
            foot + new Vector2(13f, -5f),
            foot + new Vector2(-13f, -5f),
        ];
        batch.FillConvexPolygon(body, new ColorRgba(104, 84, 64));
        batch.FillRectangle(new RectangleF(foot.X - 17f, foot.Y - 21f, 34f, 3f), new ColorRgba(70, 72, 78));
        batch.FillCircle(foot + new Vector2(-5f, -21f), 6f, ore, 10);
        batch.FillCircle(foot + new Vector2(5f, -22f), 5f, Shade(ore, 0.8f), 10);
        batch.FillCircle(foot + new Vector2(0f, -25f), 4f, Shade(ore, 1.15f), 8);
    }

    private static void DrawRuinedPillar(GeometryBatch batch, Vector2 foot, byte variant)
    {
        ColorRgba stone = (variant & 1) == 0 ? new ColorRgba(142, 146, 152) : new ColorRgba(124, 128, 134);
        float height = 34f + (variant % 3 * 8f);
        batch.FillCircle(foot + new Vector2(3f, 3f), 11f, new ColorRgba(0, 0, 0, 45), 12);
        batch.FillRectangle(new RectangleF(foot.X - 10f, foot.Y - 6f, 20f, 6f), Shade(stone, 0.78f));
        batch.FillRectangle(new RectangleF(foot.X - 7f, foot.Y - height, 14f, height - 6f), stone);
        batch.FillRectangle(new RectangleF(foot.X + 2f, foot.Y - height, 5f, height - 6f), Shade(stone, 0.84f));
        batch.FillTriangle(
            new Vector2(foot.X - 7f, foot.Y - height),
            new Vector2(foot.X + 7f, foot.Y - height),
            new Vector2(foot.X - 2f, foot.Y - height - 7f),
            Shade(stone, 1.12f));
    }

    private static void DrawWatchtower(GeometryBatch batch, Vector2 foot, float width, byte variant)
    {
        ColorRgba stone = (variant & 1) == 0 ? new ColorRgba(128, 130, 136) : new ColorRgba(136, 108, 76);
        ColorRgba timber = new(118, 78, 44);
        float hw = MathF.Max(18f, width * 0.42f);
        float height = 74f;
        batch.FillCircle(foot + new Vector2(4f, 4f), hw * 1.05f, new ColorRgba(0, 0, 0, 50), 16);
        batch.FillRectangle(new RectangleF(foot.X - hw, foot.Y - height, hw * 2f, height), stone);
        batch.FillRectangle(new RectangleF(foot.X + (hw * 0.2f), foot.Y - height, hw * 0.8f, height), Shade(stone, 0.82f));
        batch.FillRectangle(new RectangleF(foot.X - hw - 4f, foot.Y - height - 10f, (hw * 2f) + 8f, 12f), timber);
        batch.FillTriangle(
            new Vector2(foot.X - hw - 8f, foot.Y - height - 10f),
            new Vector2(foot.X + hw + 8f, foot.Y - height - 10f),
            new Vector2(foot.X, foot.Y - height - 30f),
            new ColorRgba(148, 56, 42));
        batch.FillRectangle(new RectangleF(foot.X - 6f, foot.Y - 22f, 12f, 22f), new ColorRgba(44, 30, 20));
    }

    private static ColorRgba Shade(ColorRgba c, float k) =>
        new((byte)Math.Clamp(c.R * k, 0f, 255f), (byte)Math.Clamp(c.G * k, 0f, 255f), (byte)Math.Clamp(c.B * k, 0f, 255f), c.A);
}
