// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Random;

namespace Carbon.Maps.Landmarks;

/// <summary>
/// The structures and fields of one point of interest. Built deterministically from the point of interest and
/// the pure terrain sampler, so the server (collision, navigation) and every client (rendering) agree on it.
/// </summary>
public sealed class LandmarkLayout
{
    private const float Px = 1f / 32f;

    private readonly LandmarkPiece[] _pieces;
    private readonly LandmarkField[] _fields;

    private LandmarkLayout(LandmarkPiece[] pieces, LandmarkField[] fields)
    {
        _pieces = pieces;
        _fields = fields;
    }

    /// <summary>Gets the empty layout.</summary>
    public static LandmarkLayout Empty { get; } = new([], []);

    /// <summary>Gets the upright structures.</summary>
    public ReadOnlySpan<LandmarkPiece> Pieces => _pieces;

    /// <summary>Gets the crop fields.</summary>
    public ReadOnlySpan<LandmarkField> Fields => _fields;

    /// <summary>Builds the layout of a point of interest.</summary>
    /// <param name="poi">The point of interest.</param>
    /// <param name="tileAt">Pure terrain sampler.</param>
    /// <returns>The layout.</returns>
    public static LandmarkLayout Build(in PointOfInterest poi, Func<int, int, MapTile> tileAt)
    {
        ArgumentNullException.ThrowIfNull(tileAt);
        if (poi.Type == PointOfInterestType.None)
        {
            return Empty;
        }

        List<LandmarkPiece> pieces = new();
        List<LandmarkField> fields = new();
        float cx = poi.TileX + 0.5f;
        float cy = poi.TileY + 0.5f;
        float r = poi.Radius;
        byte level = tileAt(poi.TileX, poi.TileY).HeightLevel;
        ulong seed = SpatialHash.Hash(0x1A2B3C4D5E6F7081UL, poi.TileX, poi.TileY);

        switch (poi.Type)
        {
            case PointOfInterestType.RiverVillage:
                {
                    pieces.Add(new LandmarkPiece(LandmarkPieceKind.Well, cx, cy + (10f * Px), 1f, 0));
                    int houses = 0;
                    for (int i = 0; i < 9 && houses < 7; i++)
                    {
                        float a = (i * (MathF.Tau / 9f)) + (Hash01(seed, i) * 0.35f);
                        float dist = r * (0.55f + (Hash01(seed, i + 40) * 0.22f));
                        float x = cx + (Cos(a) * dist);
                        float y = cy + (Sin(a) * dist * 0.85f);
                        float w = (64f + (Hash01(seed, i + 80) * 26f)) * Px;
                        if (IsBuildable(tileAt, x, y, w, level) && !Overlaps(pieces, x, y, w + (22f * Px)))
                        {
                            pieces.Add(new LandmarkPiece(LandmarkPieceKind.House, x, y, w, (byte)(Hash(seed, i + 120) % 6)));
                            houses++;
                        }
                    }

                    for (int i = 0; i < 6; i++)
                    {
                        float a = (i * (MathF.Tau / 6f)) + 0.5f + (Hash01(seed, i + 200) * 0.3f);
                        float fx = cx + (Cos(a) * r * 0.86f);
                        float fy = cy + (Sin(a) * r * 0.86f);
                        float hx = (36f + (Hash01(seed, i + 210) * 18f)) * Px;
                        float hy = (22f + (Hash01(seed, i + 220) * 12f)) * Px;
                        if (IsBuildable(tileAt, fx, fy, hx * 2f, level) && !Overlaps(pieces, fx, fy, hx + (40f * Px)))
                        {
                            fields.Add(new LandmarkField(fx - hx, fy - hy, fx + hx, fy + hy, (byte)(Hash(seed, i + 230) % 3)));
                        }
                    }

                    pieces.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, cx + (42f * Px), cy + (34f * Px), 1f, 0));
                    break;
                }

            case PointOfInterestType.Harbor:
                {
                    (float sx, float sy) = SeaDirection(poi);
                    float px = -sy;
                    float py = sx;
                    float ix = cx - (sx * r * 0.30f);
                    float iy = cy - (sy * r * 0.30f);
                    pieces.Add(new LandmarkPiece(LandmarkPieceKind.Warehouse, ix, iy, 104f * Px, (byte)(Hash(seed, 1) % 6)));
                    for (int i = -1; i <= 1; i += 2)
                    {
                        float x = ix + (px * i * 120f * Px) + (sx * 20f * Px);
                        float y = iy + (py * i * 120f * Px) + (sy * 20f * Px);
                        if (IsBuildable(tileAt, x, y, 64f * Px, level))
                        {
                            pieces.Add(new LandmarkPiece(LandmarkPieceKind.House, x, y, 64f * Px, (byte)(Hash(seed, i + 5) % 6)));
                        }
                    }

                    pieces.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, cx + (px * 46f * Px) + (sx * 30f * Px), cy + (py * 46f * Px) + (sy * 30f * Px), 1f, 0));
                    pieces.Add(new LandmarkPiece(LandmarkPieceKind.Barrel, cx - (px * 52f * Px) + (sx * 36f * Px), cy - (py * 52f * Px) + (sy * 36f * Px), 1f, 1));
                    break;
                }

            case PointOfInterestType.LakesideCamp:
                {
                    pieces.Add(new LandmarkPiece(LandmarkPieceKind.Campfire, cx, cy, 1f, 0));
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (-MathF.PI / 2f) + ((i - 1) * 1.1f) + (Hash01(seed, i) * 0.3f);
                        float x = cx + (Cos(a) * 76f * Px);
                        float y = cy + (Sin(a) * 62f * Px);
                        if (IsBuildable(tileAt, x, y, 56f * Px, level))
                        {
                            pieces.Add(new LandmarkPiece(LandmarkPieceKind.Tent, x, y, (56f + (Hash01(seed, i + 9) * 10f)) * Px, (byte)(Hash(seed, i + 20) % 3)));
                        }
                    }

                    break;
                }

            case PointOfInterestType.MountainShrine:
                pieces.Add(new LandmarkPiece(LandmarkPieceKind.Altar, cx, cy - (24f * Px), 1f, 0));
                pieces.Add(new LandmarkPiece(LandmarkPieceKind.ShrineGate, cx, cy + (r * 0.62f), 92f * Px, 0));
                pieces.Add(new LandmarkPiece(LandmarkPieceKind.StoneLantern, cx - (58f * Px), cy + (18f * Px), 1f, 0));
                pieces.Add(new LandmarkPiece(LandmarkPieceKind.StoneLantern, cx + (58f * Px), cy + (18f * Px), 1f, 0));
                break;

            case PointOfInterestType.None:
            default:
                break;
        }

        return new LandmarkLayout([.. pieces], [.. fields]);
    }

    /// <summary>Returns whether the solid footprint of any structure overlaps a world tile.</summary>
    /// <param name="tileX">World tile X.</param>
    /// <param name="tileY">World tile Y.</param>
    /// <returns><c>true</c> when the tile is blocked.</returns>
    public bool BlocksTile(int tileX, int tileY)
    {
        Span<TileBox> boxes = stackalloc TileBox[LandmarkPiece.MaxColliders];
        for (int i = 0; i < _pieces.Length; i++)
        {
            int n = _pieces[i].GetColliders(boxes);
            for (int k = 0; k < n; k++)
            {
                TileBox b = boxes[k];

                // Count the tile as blocked when the footprint covers a meaningful part of it.
                float ox = MathF.Min(b.MaxX, tileX + 1) - MathF.Max(b.MinX, tileX);
                float oy = MathF.Min(b.MaxY, tileY + 1) - MathF.Max(b.MinY, tileY);
                if (ox > 0.3f && oy > 0.3f)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Dry ground at the site's level under the whole footprint: no buildings in rivers or on cliff edges.
    private static bool IsBuildable(Func<int, int, MapTile> tileAt, float x, float y, float width, byte level)
    {
        for (int i = -1; i <= 1; i++)
        {
            for (int j = 0; j <= 1; j++)
            {
                int tx = (int)MathF.Floor(x + (i * width * 0.5f));
                int ty = (int)MathF.Floor(y - (j * 40f * Px));
                MapTile t = tileAt(tx, ty);
                if (t.IsSolid || t.HeightLevel != level || t.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Overlaps(List<LandmarkPiece> pieces, float x, float y, float clearance)
    {
        foreach (LandmarkPiece piece in pieces)
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

    private static (float X, float Y) SeaDirection(in PointOfInterest poi)
    {
        float len = MathF.Sqrt((poi.FacingX * poi.FacingX) + (poi.FacingY * poi.FacingY));
        return len > 0.1f ? (poi.FacingX / len, poi.FacingY / len) : (0f, 1f);
    }

    private static float Sin(float a) => DeterministicMath.Sin(a);

    private static float Cos(float a) => DeterministicMath.Cos(a);

    private static uint Hash(ulong seed, int i) => SpatialHash.Hash32(seed, i, 0, 0x4C41594FUL);

    private static float Hash01(ulong seed, int i) => (Hash(seed, i) & 0xFFFF) / 65535f;
}
