// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Procedural water-surface effects around a wading character: the waterline ring where the body breaks the
/// surface, slow ripples spreading from it, and a V-shaped wake while moving. Allocation-free; drawn in the
/// regular (ambient-darkened) pass so it never glows at night.
/// </summary>
internal static class WadingArt
{
    private const float Perspective = 0.38f; // ellipse height / width: the water plane seen from the 2.5D camera

    /// <summary>
    /// Draws everything that lies behind the body: displaced-water shade, spreading ripples, the wake and the far
    /// half of the waterline ring. Call before the character sprite.
    /// </summary>
    /// <param name="batch">Geometry batch in the world pass.</param>
    /// <param name="waterline">Point where the body meets the surface (the entity's ground position).</param>
    /// <param name="halfWidth">Half of the body width at the waterline, in world units.</param>
    /// <param name="strength">0..1 visibility (fades the effect in as the character enters the water).</param>
    /// <param name="velocity">Character velocity (world units per second).</param>
    /// <param name="time">Animation time in seconds.</param>
    public static void DrawBehind(GeometryBatch batch, Vector2 waterline, float halfWidth, float strength, Vector2 velocity, float time)
    {
        if (strength <= 0.01f)
        {
            return;
        }

        float rx = halfWidth + 3f;
        float speed = velocity.Length();
        Vector2 dir = speed > 1f ? velocity / speed : Vector2.Zero;
        float moving = Math.Clamp(speed / 120f, 0f, 1f);

        // Water pushed aside by the body reads as a slightly darker, calmer patch.
        FillEllipse(batch, waterline, rx * 1.15f, rx * 1.15f * Perspective, new ColorRgba(16, 54, 74, (byte)(46f * strength)));

        // Rings spreading outward; while moving they are left behind, trailing the character.
        for (int i = 0; i < 3; i++)
        {
            float p = Fract((time * (0.55f + (0.35f * moving))) + (i / 3f));
            float r = rx * (1f + (p * 1.6f));
            Vector2 c = waterline - (dir * (p * 14f * moving));
            float a = (1f - p) * (1f - p) * 120f * strength;
            DrawEllipseArc(batch, c, r, r * Perspective, 0f, MathF.Tau, 18, new ColorRgba(214, 236, 246, (byte)a), 1.1f);
        }

        if (moving > 0.05f)
        {
            DrawWake(batch, waterline, dir, rx, moving * strength, time);
        }

        // Far half of the waterline ring (hidden by the body in the middle).
        DrawEllipseArc(batch, waterline, rx, rx * Perspective, MathF.PI, MathF.Tau, 12, new ColorRgba(226, 242, 250, (byte)(120f * strength)), 1.4f);
    }

    /// <summary>
    /// Draws the near half of the waterline ring and its foam glints over the body. Call after the sprite.
    /// </summary>
    public static void DrawInFront(GeometryBatch batch, Vector2 waterline, float halfWidth, float strength, Vector2 velocity, float time)
    {
        if (strength <= 0.01f)
        {
            return;
        }

        float rx = halfWidth + 3f;
        float bob = MathF.Sin(time * 3.1f) * 0.6f;
        Vector2 c = waterline + new Vector2(0f, bob);
        float moving = Math.Clamp(velocity.Length() / 120f, 0f, 1f);

        DrawEllipseArc(batch, c, rx, rx * Perspective, 0f, MathF.PI, 12, new ColorRgba(236, 248, 252, (byte)(190f * strength)), 1.7f);

        // A darker band just under the ring separates surface from submerged body.
        DrawEllipseArc(batch, c + new Vector2(0f, 1.6f), rx * 0.92f, rx * 0.92f * Perspective, 0.25f, MathF.PI - 0.25f, 10, new ColorRgba(18, 60, 82, (byte)(90f * strength)), 1.4f);

        // Foam glints that slide around the ring; busier while moving.
        int glints = moving > 0.05f ? 4 : 2;
        for (int i = 0; i < glints; i++)
        {
            float angle = (time * (0.8f + moving)) + (i * MathF.Tau / glints);
            float s = MathF.Sin(angle);
            if (s < 0f)
            {
                continue; // only the near side
            }

            Vector2 p = c + new Vector2(MathF.Cos(angle) * rx, s * rx * Perspective);
            batch.FillCircle(p, 1.1f + (0.6f * moving), new ColorRgba(248, 252, 255, (byte)(200f * strength * s)), 6);
        }
    }

    private static void DrawWake(GeometryBatch batch, Vector2 origin, Vector2 dir, float rx, float strength, float time)
    {
        Vector2 back = -dir;
        Vector2 side = new(-dir.Y, dir.X);
        float length = 18f + (16f * strength);

        // Two arms of a V opening behind the character.
        for (int arm = -1; arm <= 1; arm += 2)
        {
            Vector2 start = origin + (side * (arm * rx * 0.7f)) + new Vector2(0f, 1f);
            Vector2 armDir = Vector2.Normalize(back + (side * (arm * 0.55f)));
            const int Segments = 4;
            for (int s = 0; s < Segments; s++)
            {
                float t0 = s / (float)Segments;
                float t1 = (s + 1) / (float)Segments;
                Vector2 a = start + (armDir * (length * t0));
                Vector2 b = start + (armDir * (length * t1));
                a.Y = origin.Y + ((a.Y - origin.Y) * Perspective * 2f);
                b.Y = origin.Y + ((b.Y - origin.Y) * Perspective * 2f);
                batch.DrawLine(a, b, new ColorRgba(226, 242, 250, (byte)(185f * strength * (1f - t0))), 1.7f);
            }
        }

        // Foam flecks drifting back along the wake.
        for (int k = 0; k < 4; k++)
        {
            float t = Fract((time * 1.8f) + (k * 0.25f));
            float arm = (k & 1) == 0 ? -1f : 1f;
            Vector2 p = origin + (back * (t * length)) + (side * (arm * ((rx * 0.5f) + (t * 8f))));
            p.Y = origin.Y + ((p.Y - origin.Y) * Perspective * 2f);
            batch.FillCircle(p, (1.4f * (1f - t)) + 0.4f, new ColorRgba(240, 248, 252, (byte)(170f * strength * (1f - t))), 6);
        }
    }

    private static void FillEllipse(GeometryBatch batch, Vector2 c, float rx, float ry, ColorRgba color)
    {
        const int Segments = 20;
        Span<Vector2> points = stackalloc Vector2[Segments];
        for (int i = 0; i < Segments; i++)
        {
            float a = i * MathF.Tau / Segments;
            points[i] = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
        }

        batch.FillConvexPolygon(points, color);
    }

    private static void DrawEllipseArc(GeometryBatch batch, Vector2 c, float rx, float ry, float from, float to, int segments, ColorRgba color, float thickness)
    {
        if (color.A == 0)
        {
            return;
        }

        Vector2 prev = c + new Vector2(MathF.Cos(from) * rx, MathF.Sin(from) * ry);
        for (int i = 1; i <= segments; i++)
        {
            float a = from + ((to - from) * i / segments);
            Vector2 next = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
            batch.DrawLine(prev, next, color, thickness);
            prev = next;
        }
    }

    private static float Fract(float v) => v - MathF.Floor(v);
}
