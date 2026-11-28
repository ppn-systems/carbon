// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;

namespace Carbon.Desktop.Menu;

/// <summary>An icon drawn at the left edge of a menu text field.</summary>
internal enum MenuFieldIcon
{
    None,
    User,
    Lock,
    Globe,
    Hero,
}

/// <summary>
/// Small vector icons of the account menus (field icons, show-password eye, validation marks). Drawn with
/// <see cref="GeometryBatch"/> primitives only: no textures, no allocation.
/// </summary>
internal static class MenuIcons
{
    /// <summary>Draws a field icon centred on <paramref name="c"/>, about 16 px tall.</summary>
    public static void DrawField(GeometryBatch batch, MenuFieldIcon icon, Vector2 c, ColorRgba color)
    {
        switch (icon)
        {
            case MenuFieldIcon.User:
                batch.FillCircle(c + new Vector2(0f, -4f), 3.6f, color, segments: 12);
                batch.FillTriangle(c + new Vector2(-7f, 8f), c + new Vector2(0f, 0f), c + new Vector2(7f, 8f), color);
                batch.FillRectangle(new RectangleF(c.X - 5f, c.Y + 2f, 10f, 6f), color);
                break;
            case MenuFieldIcon.Lock:
                batch.DrawCircle(c + new Vector2(0f, -2f), 4.2f, color, segments: 12, thickness: 2f);
                batch.FillRectangle(new RectangleF(c.X - 6f, c.Y - 1f, 12f, 9f), color);
                break;
            case MenuFieldIcon.Globe:
                batch.DrawCircle(c, 7f, color, segments: 16, thickness: 1.5f);
                batch.DrawLine(c + new Vector2(-7f, 0f), c + new Vector2(7f, 0f), color, 1.5f);
                batch.DrawLine(c + new Vector2(0f, -7f), c + new Vector2(0f, 7f), color, 1.5f);
                break;
            case MenuFieldIcon.Hero:
                batch.DrawLine(c + new Vector2(-6f, 7f), c + new Vector2(6f, -7f), color, 2.5f);
                batch.DrawLine(c + new Vector2(-6f, 1f), c + new Vector2(0f, 7f), color, 2f);
                break;
            case MenuFieldIcon.None:
            default:
                break;
        }
    }

    /// <summary>Draws the show-password eye; <paramref name="revealed"/> draws it open, otherwise struck through.</summary>
    public static void DrawEye(GeometryBatch batch, Vector2 c, bool revealed, ColorRgba color)
    {
        ReadOnlySpan<Vector2> lid =
        [
            c + new Vector2(-9f, 0f),
            c + new Vector2(-4f, -5f),
            c + new Vector2(4f, -5f),
            c + new Vector2(9f, 0f),
            c + new Vector2(4f, 5f),
            c + new Vector2(-4f, 5f),
        ];
        for (int i = 0; i < lid.Length; i++)
        {
            batch.DrawLine(lid[i], lid[(i + 1) % lid.Length], color, 1.6f);
        }

        batch.FillCircle(c, 2.6f, color, segments: 10);
        if (!revealed)
        {
            batch.DrawLine(c + new Vector2(-8f, 7f), c + new Vector2(8f, -7f), color, 2f);
        }
    }

    /// <summary>Draws a check mark (valid field).</summary>
    public static void DrawCheck(GeometryBatch batch, Vector2 c, ColorRgba color)
    {
        batch.DrawLine(c + new Vector2(-6f, 0f), c + new Vector2(-2f, 5f), color, 2.5f);
        batch.DrawLine(c + new Vector2(-2f, 5f), c + new Vector2(6f, -5f), color, 2.5f);
    }

    /// <summary>Draws a round "!" badge (inline error).</summary>
    public static void DrawAlert(GeometryBatch batch, Vector2 c, ColorRgba color)
    {
        batch.FillCircle(c, 6f, color, segments: 14);
        ColorRgba ink = new(20, 12, 14, 255);
        batch.FillRectangle(new RectangleF(c.X - 1f, c.Y - 4f, 2f, 5f), ink);
        batch.FillRectangle(new RectangleF(c.X - 1f, c.Y + 2f, 2f, 2f), ink);
    }

    /// <summary>Draws the caps-lock glyph: an up arrow over a bar.</summary>
    public static void DrawCapsLock(GeometryBatch batch, Vector2 c, ColorRgba color)
    {
        batch.FillTriangle(c + new Vector2(-6f, 0f), c + new Vector2(0f, -7f), c + new Vector2(6f, 0f), color);
        batch.FillRectangle(new RectangleF(c.X - 2.5f, c.Y, 5f, 3f), color);
        batch.FillRectangle(new RectangleF(c.X - 5f, c.Y + 5f, 10f, 2f), color);
    }

    /// <summary>Draws a ring of dots turning with <paramref name="time"/> (busy spinner).</summary>
    public static void DrawSpinner(GeometryBatch batch, Vector2 c, float radius, float time, ColorRgba color)
    {
        const int dots = 8;
        float head = time * 10f;
        for (int i = 0; i < dots; i++)
        {
            float angle = i * MathF.Tau / dots;
            float age = (((head - i) % dots) + dots) % dots;
            float alpha = 1f - (age / dots);
            Vector2 p = c + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
            batch.FillCircle(p, 1.8f, MenuPalette.WithAlpha(color, 0.25f + (alpha * 0.75f)), segments: 6);
        }
    }

    /// <summary>Draws a small diamond (panel header ornament).</summary>
    public static void DrawDiamond(GeometryBatch batch, Vector2 c, float r, ColorRgba color)
    {
        batch.FillTriangle(c + new Vector2(-r, 0f), c + new Vector2(0f, -r), c + new Vector2(r, 0f), color);
        batch.FillTriangle(c + new Vector2(-r, 0f), c + new Vector2(r, 0f), c + new Vector2(0f, r), color);
    }
}
