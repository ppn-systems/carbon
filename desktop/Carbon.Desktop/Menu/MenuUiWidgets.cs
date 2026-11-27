// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Menu;

/// <summary>A "Remember me" style checkbox: a square with a check, then its label (and an optional note under it).</summary>
internal sealed class MenuCheckbox
{
    private float _time;

    public MenuCheckbox(string label) => this.Label = label;

    public string Label { get; }

    /// <summary>Gets or sets a muted line under the label (e.g. what remembering stores).</summary>
    public string Note { get; set; } = string.Empty;

    public bool IsChecked { get; set; }

    public bool IsFocused { get; set; }

    /// <summary>Gets whether the pointer is over the row.</summary>
    public bool IsHovered { get; private set; }

    /// <summary>Gets or sets the clickable row (box plus label).</summary>
    public RectangleF Bounds { get; set; }

    private RectangleF Box => new(this.Bounds.X, this.Bounds.Y + MathF.Floor((this.Bounds.Height - 22f) * 0.5f), 22f, 22f);

    /// <summary>Toggles on a click inside the row; returns whether it was clicked.</summary>
    public bool Update(Vector2 mousePos, bool leftPressed, float dt = 0f)
    {
        _time += Math.Max(0f, dt);
        this.IsHovered = this.Bounds.Contains(mousePos);
        if (leftPressed && this.IsHovered)
        {
            this.IsChecked = !this.IsChecked;
            return true;
        }

        return false;
    }

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        RectangleF box = this.Box;
        ColorRgba border = this.IsFocused || this.IsChecked ? CarbonUiPalette.BorderActive : this.IsHovered ? MenuPalette.HoverBorder : CarbonUiPalette.BorderSubtle;
        skin.DrawInset(spriteBatch, in box, this.IsFocused || this.IsHovered ? CarbonUiPalette.SlotSelectedFill : CarbonUiPalette.SlotFill, border);
    }

    public void DrawGeometry(GeometryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        RectangleF box = this.Box;
        if (this.IsChecked)
        {
            batch.DrawLine(new Vector2(box.X + 5f, box.Y + 11f), new Vector2(box.X + 9f, box.Y + 16f), CarbonUiPalette.BorderActive, 3f);
            batch.DrawLine(new Vector2(box.X + 9f, box.Y + 16f), new Vector2(box.X + 17f, box.Y + 6f), CarbonUiPalette.BorderActive, 3f);
        }

        if (this.IsFocused)
        {
            MenuButton.DrawFocusRing(batch, in box, _time);
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        RectangleF box = this.Box;
        float ty = MathF.Floor(this.Bounds.Y + ((this.Bounds.Height - font.LineHeight) * 0.5f));
        spriteBatch.DrawOutlinedString(font, this.Label, new Vector2(box.Right + 10f, ty), this.IsFocused ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary, CarbonUiPalette.TextOutline, 1, 1f);
        if (this.Note.Length > 0)
        {
            float x = box.Right + 10f;
            string note = CarbonUiSkin.TruncateToWidth(font, this.Note, this.Bounds.Right - x, 1f);
            spriteBatch.DrawOutlinedString(font, note, new Vector2(x, ty + font.LineHeight + 1f), CarbonUiPalette.TextMuted, CarbonUiPalette.TextOutline, 1, 1f);
        }
    }
}

internal static class MenuBackdropRenderer
{
    public static void Draw(GeometryBatch batch, int viewportWidth, int viewportHeight, float elapsed)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        // Layered twilight sky gradient bands
        const int bands = 8;
        float bandH = MathF.Ceiling(viewportHeight / (float)bands);
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)(bands - 1);
            byte r = (byte)(10 + (t * 16));
            byte g = (byte)(16 + (t * 28));
            byte b = (byte)(36 + (t * 32));
            batch.FillRectangle(new RectangleF(0f, i * bandH, viewportWidth, bandH + 1f), new ColorRgba(r, g, b, 255));
        }

        // Drifting stars & fireflies
        for (int i = 0; i < 28; i++)
        {
            int modX = i * 197 % 997;
            int modY = i * 313 % 787;
            float seedX = modX / 997f;
            float seedY = modY / 787f;
            float px = ((seedX * viewportWidth) + (elapsed * (6f + (i % 5 * 3f)))) % viewportWidth;
            float py = (seedY * viewportHeight * 0.72f) + (MathF.Sin((elapsed * 1.4f) + i) * 5f);
            byte alpha = (byte)(110 + (int)(MathF.Sin((elapsed * 2.2f) + i) * 70f));
            ColorRgba glow = (i % 3 == 0)
                ? new ColorRgba(250, 204, 21, alpha)
                : new ColorRgba(148, 210, 255, alpha);
            float sz = (i % 4 == 0) ? 4f : 2.5f;
            batch.FillRectangle(new RectangleF(px, py, sz, sz), glow);
        }

        // Distant mountain & treeline silhouettes along the horizon
        const int ridgeSteps = 18;
        float segW = MathF.Ceiling(viewportWidth / (float)ridgeSteps);
        float baseH = viewportHeight * 0.78f;
        for (int i = 0; i < ridgeSteps; i++)
        {
            float peak = 36f + (MathF.Abs(MathF.Sin((i * 0.85f) + 0.4f)) * 90f);
            float x0 = i * segW;
            batch.FillTriangle(
                new Vector2(x0 - 10f, viewportHeight),
                new Vector2(x0 + (segW * 0.5f), baseH - peak),
                new Vector2(x0 + segW + 10f, viewportHeight),
                new ColorRgba(14, 26, 40, 240));
        }

        batch.FillRectangle(new RectangleF(0f, viewportHeight - 54f, viewportWidth, 54f), new ColorRgba(9, 17, 26, 255));
    }
}
