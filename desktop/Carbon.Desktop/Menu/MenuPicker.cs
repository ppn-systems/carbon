// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Menu;

/// <summary>
/// One option row of the character creator (docs/art/menu-ui.md rule 18): <c>◀ LABEL: value  n/N ▶</c>. The arrows
/// (or Left/Right while the row has focus) step through the choices, wrapping. The row only knows a count and an
/// index; the scene owns what the choices mean (traits today, races.json options later) and sets the shown value.
/// </summary>
internal sealed class MenuPicker
{
    private const float ArrowWidth = 28f;
    private readonly MenuButton _prev = new(string.Empty, MenuButtonStyle.NeutralSlate);
    private readonly MenuButton _next = new(string.Empty, MenuButtonStyle.NeutralSlate);
    private float _time;

    public MenuPicker(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        this.Count = count;
    }

    /// <summary>Gets the number of choices.</summary>
    public int Count { get; private set; }

    /// <summary>Gets or sets the chosen index (clamped into range).</summary>
    public int Index
    {
        get;
        set => field = Math.Clamp(value, 0, this.Count - 1);
    }

    /// <summary>Gets or sets the row text, e.g. "HAIR: CROP" (already localized and formatted by the scene).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the "n/N" text (formatted by the scene so it stays localized).</summary>
    public string IndexText { get; set; } = string.Empty;

    /// <summary>Gets or sets the whole row's rectangle.</summary>
    public RectangleF Bounds { get; set; }

    public bool IsFocused { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets the previous-choice arrow's rectangle.</summary>
    public RectangleF PrevBounds => new(this.Bounds.X, this.Bounds.Y, ArrowWidth, this.Bounds.Height);

    /// <summary>Gets the next-choice arrow's rectangle.</summary>
    public RectangleF NextBounds => new(this.Bounds.Right - ArrowWidth, this.Bounds.Y, ArrowWidth, this.Bounds.Height);

    /// <summary>Changes how many choices there are (e.g. a race with fewer options); the index is clamped.</summary>
    public void SetCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        this.Count = count;
        int current = this.Index;
        this.Index = current;
    }

    /// <summary>Steps by <paramref name="step"/> (±1), wrapping; returns whether the index changed.</summary>
    public bool Step(int step)
    {
        if (!this.IsEnabled || step == 0 || this.Count < 2)
        {
            return false;
        }

        this.Index = (((this.Index + step) % this.Count) + this.Count) % this.Count;
        return true;
    }

    /// <summary>Tracks the pointer; a click on an arrow steps. Returns the step taken (-1, 0 or +1).</summary>
    public int Update(Vector2 mousePos, bool leftDown, bool leftPressed, float dt)
    {
        _time += Math.Max(0f, dt);
        _prev.Bounds = this.PrevBounds;
        _next.Bounds = this.NextBounds;
        _prev.IsEnabled = this.IsEnabled;
        _next.IsEnabled = this.IsEnabled;
        if (_prev.Update(mousePos, leftDown, leftPressed, dt) && this.Step(-1))
        {
            return -1;
        }

        return _next.Update(mousePos, leftDown, leftPressed, dt) && this.Step(1) ? 1 : 0;
    }

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        _prev.DrawSkin(spriteBatch, skin);
        _next.DrawSkin(spriteBatch, skin);
    }

    /// <summary>Draws the arrow glyphs, the bevels and, when focused, the shared focus ring around the row.</summary>
    public void DrawOverlay(GeometryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        _prev.DrawOverlay(batch);
        _next.DrawOverlay(batch);
        ColorRgba arrow = this.IsEnabled ? CarbonUiPalette.TextPrimary : MenuPalette.DisabledText;
        DrawArrow(batch, this.PrevBounds, left: true, arrow);
        DrawArrow(batch, this.NextBounds, left: false, arrow);
        if (this.IsFocused)
        {
            MenuButton.DrawFocusRing(batch, this.Bounds, _time);
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        ColorRgba black = CarbonUiPalette.TextOutline;
        float ty = MathF.Floor(this.Bounds.Y + ((this.Bounds.Height - font.LineHeight) * 0.5f));
        TextMeasureResult im = font.MeasureString(this.IndexText, TextLayoutOptions.Default, 1f);
        float indexX = MathF.Floor(this.NextBounds.X - 8f - im.Width);
        float textX = this.PrevBounds.Right + 8f;
        string text = CarbonUiSkin.TruncateToWidth(font, this.Text, indexX - textX - 8f, 1f);
        spriteBatch.DrawOutlinedString(font, text, new Vector2(textX, ty), this.IsFocused ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, this.IndexText, new Vector2(indexX, ty), CarbonUiPalette.TextMuted, black, 1, 1f);
    }

    private static void DrawArrow(GeometryBatch batch, in RectangleF b, bool left, ColorRgba color)
    {
        Vector2 c = new(b.X + (b.Width * 0.5f), b.Y + (b.Height * 0.5f));
        float d = left ? -1f : 1f;
        batch.FillTriangle(c + new Vector2(4f * d, 0f), c + new Vector2(-3f * d, -5f), c + new Vector2(-3f * d, 5f), color);
    }
}
