// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Procedural;

namespace Carbon.Desktop.Rendering.VectorArt;

/// <summary>
/// Zero-allocation baker stamping compiled <c>[0..32]</c> tile vector blueprints (biome micro-details &amp; structures)
/// directly into static chunk VBOs via <see cref="ChunkMeshBuilder"/>.
/// </summary>
internal static class VectorMeshBaker
{
    private const int EllipseSegments = 10;
    private static readonly Vector2[] s_unitCircle = BuildUnitCircle(EllipseSegments);

    public static void StampTileShapes(
        ChunkMeshBuilder builder,
        ReadOnlySpan<CompiledVectorShape> shapes,
        float tileWorldX,
        float tileWorldY,
        float tileSize,
        bool mirrorX = false)
    {
        float scale = tileSize / 32f;
        for (int i = 0; i < shapes.Length; i++)
        {
            ref readonly CompiledVectorShape s = ref shapes[i];
            switch (s.Kind)
            {
                case VectorShapeKind.Rect:
                    StampRect(builder, in s, tileWorldX, tileWorldY, scale, mirrorX);
                    break;

                case VectorShapeKind.Ellipse:
                    StampEllipse(builder, in s, tileWorldX, tileWorldY, scale, mirrorX);
                    break;

                case VectorShapeKind.Polygon:
                    StampPolygon(builder, in s, tileWorldX, tileWorldY, scale, mirrorX);
                    break;

                case VectorShapeKind.Capsule:
                    StampSegment(builder, MapPoint(s.P0, tileWorldX, tileWorldY, scale, mirrorX), MapPoint(s.P1, tileWorldX, tileWorldY, scale, mirrorX), s.Thickness * scale, s.Color, s.HasOutline, s.OutlineColor, s.OutlineThickness * scale, capEnds: true);
                    break;

                case VectorShapeKind.Line:
                    StampSegment(builder, MapPoint(s.P0, tileWorldX, tileWorldY, scale, mirrorX), MapPoint(s.P1, tileWorldX, tileWorldY, scale, mirrorX), s.Thickness * scale, s.Color, hasOutline: false, default, 0f, capEnds: false);
                    break;

                case VectorShapeKind.Ring:
                    StampRing(builder, MapPoint(s.P0, tileWorldX, tileWorldY, scale, mirrorX), s.P1.X * scale, s.Thickness * scale, s.Color);
                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Stamps art authored over a <paramref name="tilesWide"/> × <paramref name="tilesHigh"/> footprint
    /// (<c>[0..32·w] × [0..32·h]</c>) at a structure anchor, turned by <paramref name="turns"/> clockwise quarter turns
    /// (<c>StructureRotation</c>): one turn maps the authored footprint onto an <c>h × w</c> one. Every shape is reduced
    /// to triangles in authored space and then transformed, so rotated rectangles and ellipses stay exact.
    /// </summary>
    public static void StampFootprintShapes(
        ChunkMeshBuilder builder,
        ReadOnlySpan<CompiledVectorShape> shapes,
        float anchorWorldX,
        float anchorWorldY,
        float tileSize,
        int tilesWide,
        int tilesHigh,
        int turns)
    {
        FootprintTransform t = new(anchorWorldX, anchorWorldY, tileSize / 32f, tilesWide * 32f, tilesHigh * 32f, turns & 3);
        for (int i = 0; i < shapes.Length; i++)
        {
            ref readonly CompiledVectorShape s = ref shapes[i];
            switch (s.Kind)
            {
                case VectorShapeKind.Rect:
                    {
                        Vector2 c = new(s.P0.X + (s.P1.X * 0.5f), s.P0.Y + (s.P1.Y * 0.5f));
                        float cos = MathF.Cos(s.RotRad);
                        float sin = MathF.Sin(s.RotRad);
                        if (s.HasOutline)
                        {
                            StampQuad(builder, t, c, (s.P1.X * 0.5f) + s.OutlineThickness, (s.P1.Y * 0.5f) + s.OutlineThickness, cos, sin, s.OutlineColor);
                        }

                        StampQuad(builder, t, c, s.P1.X * 0.5f, s.P1.Y * 0.5f, cos, sin, s.Color);
                        break;
                    }

                case VectorShapeKind.Ellipse:
                    {
                        float cos = MathF.Cos(s.RotRad);
                        float sin = MathF.Sin(s.RotRad);
                        if (s.HasOutline)
                        {
                            StampFan(builder, t, s.P0, s.P1.X + s.OutlineThickness, s.P1.Y + s.OutlineThickness, cos, sin, s.OutlineColor);
                        }

                        StampFan(builder, t, s.P0, s.P1.X, s.P1.Y, cos, sin, s.Color);
                        break;
                    }

                case VectorShapeKind.Polygon:
                    {
                        Vector2[]? pts = s.PolygonPoints;
                        if (pts is null || pts.Length < 3)
                        {
                            break;
                        }

                        Vector2 v0 = t.Map(pts[0]);
                        for (int k = 1; k < pts.Length - 1; k++)
                        {
                            builder.AddTriangle(v0, t.Map(pts[k]), t.Map(pts[k + 1]), s.Color);
                        }

                        if (s.HasOutline)
                        {
                            float thick = MathF.Max(0.8f, s.OutlineThickness * t.Scale);
                            for (int k = 0; k < pts.Length; k++)
                            {
                                StampSegment(builder, t.Map(pts[k]), t.Map(pts[(k + 1) % pts.Length]), thick, s.OutlineColor, hasOutline: false, default, 0f, capEnds: false);
                            }
                        }

                        break;
                    }

                case VectorShapeKind.Capsule:
                    StampSegment(builder, t.Map(s.P0), t.Map(s.P1), s.Thickness * t.Scale, s.Color, s.HasOutline, s.OutlineColor, s.OutlineThickness * t.Scale, capEnds: true);
                    break;

                case VectorShapeKind.Line:
                    StampSegment(builder, t.Map(s.P0), t.Map(s.P1), s.Thickness * t.Scale, s.Color, hasOutline: false, default, 0f, capEnds: false);
                    break;

                case VectorShapeKind.Ring:
                    StampRing(builder, t.Map(s.P0), s.P1.X * t.Scale, s.Thickness * t.Scale, s.Color);
                    break;

                default:
                    break;
            }
        }
    }

    private static void StampQuad(ChunkMeshBuilder builder, in FootprintTransform t, Vector2 c, float hw, float hh, float cos, float sin, ColorRgba col)
    {
        Vector2 p0 = t.Map(new Vector2(c.X + (-hw * cos) - (-hh * sin), c.Y + (-hw * sin) + (-hh * cos)));
        Vector2 p1 = t.Map(new Vector2(c.X + (hw * cos) - (-hh * sin), c.Y + (hw * sin) + (-hh * cos)));
        Vector2 p2 = t.Map(new Vector2(c.X + (hw * cos) - (hh * sin), c.Y + (hw * sin) + (hh * cos)));
        Vector2 p3 = t.Map(new Vector2(c.X + (-hw * cos) - (hh * sin), c.Y + (-hw * sin) + (hh * cos)));
        builder.AddTriangle(p0, p1, p2, col);
        builder.AddTriangle(p0, p2, p3, col);
    }

    private static void StampFan(ChunkMeshBuilder builder, in FootprintTransform t, Vector2 c, float rx, float ry, float cos, float sin, ColorRgba col)
    {
        Vector2 center = t.Map(c);
        for (int i = 0; i < EllipseSegments; i++)
        {
            Vector2 u0 = s_unitCircle[i];
            Vector2 u1 = s_unitCircle[(i + 1) % EllipseSegments];
            Vector2 p0 = new(c.X + (u0.X * rx * cos) - (u0.Y * ry * sin), c.Y + (u0.X * rx * sin) + (u0.Y * ry * cos));
            Vector2 p1 = new(c.X + (u1.X * rx * cos) - (u1.Y * ry * sin), c.Y + (u1.X * rx * sin) + (u1.Y * ry * cos));
            builder.AddTriangle(center, t.Map(p0), t.Map(p1), col);
        }
    }

    /// <summary>Maps authored footprint pixels to world space for a quarter-turn rotation.</summary>
    private readonly record struct FootprintTransform(float OriginX, float OriginY, float Scale, float Width, float Height, int Turns)
    {
        public Vector2 Map(Vector2 p)
        {
            Vector2 r = this.Turns switch
            {
                1 => new Vector2(this.Height - p.Y, p.X),
                2 => new Vector2(this.Width - p.X, this.Height - p.Y),
                3 => new Vector2(p.Y, this.Width - p.X),
                _ => p,
            };
            return new Vector2(this.OriginX + (r.X * this.Scale), this.OriginY + (r.Y * this.Scale));
        }
    }

    private static void StampRect(
        ChunkMeshBuilder builder,
        in CompiledVectorShape s,
        float ox,
        float oy,
        float scale,
        bool mirrorX)
    {
        float rx = mirrorX ? 32f - (s.P0.X + s.P1.X) : s.P0.X;
        float ry = s.P0.Y;
        float rw = s.P1.X * scale;
        float rh = s.P1.Y * scale;
        float wx = ox + (rx * scale);
        float wy = oy + (ry * scale);

        if (MathF.Abs(s.RotRad) < 0.001f)
        {
            if (s.HasOutline)
            {
                float pad = s.OutlineThickness * scale;
                builder.AddRectangle(new RectangleF(wx - pad, wy - pad, rw + (pad * 2f), rh + (pad * 2f)), s.OutlineColor);
            }

            builder.AddRectangle(new RectangleF(wx, wy, rw, rh), s.Color);
            return;
        }

        Vector2 c = new(wx + (rw * 0.5f), wy + (rh * 0.5f));
        float rot = mirrorX ? -s.RotRad : s.RotRad;
        float cos = MathF.Cos(rot);
        float sin = MathF.Sin(rot);
        if (s.HasOutline)
        {
            float pad = s.OutlineThickness * scale;
            StampRotatedQuad(builder, c, (rw * 0.5f) + pad, (rh * 0.5f) + pad, cos, sin, s.OutlineColor);
        }

        StampRotatedQuad(builder, c, rw * 0.5f, rh * 0.5f, cos, sin, s.Color);
    }

    private static void StampRotatedQuad(ChunkMeshBuilder builder, Vector2 c, float hw, float hh, float cos, float sin, ColorRgba col)
    {
        Vector2 p0 = new(c.X + (-hw * cos) - (-hh * sin), c.Y + (-hw * sin) + (-hh * cos));
        Vector2 p1 = new(c.X + (hw * cos) - (-hh * sin), c.Y + (hw * sin) + (-hh * cos));
        Vector2 p2 = new(c.X + (hw * cos) - (hh * sin), c.Y + (hw * sin) + (hh * cos));
        Vector2 p3 = new(c.X + (-hw * cos) - (hh * sin), c.Y + (-hw * sin) + (hh * cos));
        builder.AddTriangle(p0, p1, p2, col);
        builder.AddTriangle(p0, p2, p3, col);
    }

    private static void StampEllipse(
        ChunkMeshBuilder builder,
        in CompiledVectorShape s,
        float ox,
        float oy,
        float scale,
        bool mirrorX)
    {
        Vector2 center = MapPoint(s.P0, ox, oy, scale, mirrorX);
        float rx = s.P1.X * scale;
        float ry = s.P1.Y * scale;
        float rot = mirrorX ? -s.RotRad : s.RotRad;
        float cos = MathF.Cos(rot);
        float sin = MathF.Sin(rot);

        if (s.HasOutline)
        {
            float pad = s.OutlineThickness * scale;
            StampEllipseFan(builder, center, rx + pad, ry + pad, cos, sin, s.OutlineColor);
        }

        StampEllipseFan(builder, center, rx, ry, cos, sin, s.Color);
    }

    private static void StampEllipseFan(ChunkMeshBuilder builder, Vector2 c, float rx, float ry, float cos, float sin, ColorRgba col)
    {
        for (int i = 0; i < EllipseSegments; i++)
        {
            Vector2 u0 = s_unitCircle[i];
            Vector2 u1 = s_unitCircle[(i + 1) % EllipseSegments];
            Vector2 p0 = new(c.X + (u0.X * rx * cos) - (u0.Y * ry * sin), c.Y + (u0.X * rx * sin) + (u0.Y * ry * cos));
            Vector2 p1 = new(c.X + (u1.X * rx * cos) - (u1.Y * ry * sin), c.Y + (u1.X * rx * sin) + (u1.Y * ry * cos));
            builder.AddTriangle(c, p0, p1, col);
        }
    }

    private static void StampPolygon(
        ChunkMeshBuilder builder,
        in CompiledVectorShape s,
        float ox,
        float oy,
        float scale,
        bool mirrorX)
    {
        Vector2[]? pts = s.PolygonPoints;
        if (pts is null || pts.Length < 3)
        {
            return;
        }

        Vector2 v0 = MapPoint(pts[0], ox, oy, scale, mirrorX);
        for (int i = 1; i < pts.Length - 1; i++)
        {
            Vector2 v1 = MapPoint(pts[i], ox, oy, scale, mirrorX);
            Vector2 v2 = MapPoint(pts[i + 1], ox, oy, scale, mirrorX);
            builder.AddTriangle(v0, v1, v2, s.Color);
        }

        if (s.HasOutline)
        {
            float thick = MathF.Max(0.8f, s.OutlineThickness * scale);
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 a = MapPoint(pts[i], ox, oy, scale, mirrorX);
                Vector2 b = MapPoint(pts[(i + 1) % pts.Length], ox, oy, scale, mirrorX);
                StampSegment(builder, a, b, thick, s.OutlineColor, hasOutline: false, default, 0f, capEnds: false);
            }
        }
    }

    private static void StampSegment(
        ChunkMeshBuilder builder,
        Vector2 a,
        Vector2 b,
        float thickness,
        ColorRgba fill,
        bool hasOutline,
        ColorRgba outline,
        float outlineThick,
        bool capEnds)
    {
        if (hasOutline)
        {
            StampQuadLine(builder, a, b, thickness + (outlineThick * 1.4f), outline, capEnds);
        }

        StampQuadLine(builder, a, b, thickness, fill, capEnds);
    }

    private static void StampQuadLine(ChunkMeshBuilder builder, Vector2 a, Vector2 b, float thickness, ColorRgba col, bool capEnds)
    {
        Vector2 delta = b - a;
        float len = delta.Length();
        if (len <= 0.0001f)
        {
            return;
        }

        Vector2 dir = delta / len;
        Vector2 perp = new(-dir.Y * (thickness * 0.5f), dir.X * (thickness * 0.5f));
        Vector2 p0 = a + perp;
        Vector2 p1 = b + perp;
        Vector2 p2 = b - perp;
        Vector2 p3 = a - perp;
        builder.AddTriangle(p0, p1, p2, col);
        builder.AddTriangle(p0, p2, p3, col);

        if (capEnds)
        {
            float r = thickness * 0.5f;
            builder.AddTriangle(p0, p3, a - (dir * r), col);
            builder.AddTriangle(p1, p2, b + (dir * r), col);
        }
    }

    private static void StampRing(ChunkMeshBuilder builder, Vector2 center, float radius, float thickness, ColorRgba col)
    {
        float rInner = MathF.Max(0.4f, radius - (thickness * 0.5f));
        float rOuter = radius + (thickness * 0.5f);
        for (int i = 0; i < EllipseSegments; i++)
        {
            Vector2 u0 = s_unitCircle[i];
            Vector2 u1 = s_unitCircle[(i + 1) % EllipseSegments];
            Vector2 i0 = center + (u0 * rInner);
            Vector2 o0 = center + (u0 * rOuter);
            Vector2 o1 = center + (u1 * rOuter);
            Vector2 i1 = center + (u1 * rInner);
            builder.AddTriangle(i0, o0, o1, col);
            builder.AddTriangle(i0, o1, i1, col);
        }
    }

    private static Vector2 MapPoint(Vector2 pt, float ox, float oy, float scale, bool mirrorX) =>
        new(ox + ((mirrorX ? 32f - pt.X : pt.X) * scale), oy + (pt.Y * scale));

    private static Vector2[] BuildUnitCircle(int count)
    {
        Vector2[] arr = new Vector2[count];
        float step = MathF.Tau / count;
        for (int i = 0; i < count; i++)
        {
            float a = i * step;
            arr[i] = new Vector2(MathF.Cos(a), MathF.Sin(a));
        }

        return arr;
    }
}
