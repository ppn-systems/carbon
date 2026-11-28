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
/// A two-state segmented toggle (docs/art/menu-ui.md rule 18), e.g. a body type: two halves side by side, the chosen one
/// lit gold. Click a half, or Left/Right / Space while focused. Labels are localized by the scene.
/// </summary>
internal sealed class MenuToggle
{
    private float _time;

    public MenuToggle(string first, string second)
    {
        this.First = first ?? string.Empty;
        this.Second = second ?? string.Empty;
    }

    /// <summary>Gets or sets the left option's label (localized by the scene).</summary>
    public string First { get; set; }

    /// <summary>Gets or sets the right option's label (localized by the scene).</summary>
    public string Second { get; set; }

    /// <summary>Gets or sets the chosen side: 0 = first, 1 = second.</summary>
    public int Selected
    {
        get;
        set => field = value == 0 ? 0 : 1;
    }

    public RectangleF Bounds { get; set; }

    public bool IsFocused { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets the half the pointer is over (-1 for none).</summary>
    public int Hovered { get; private set; } = -1;

    /// <summary>Gets the rectangle of side <paramref name="side"/> (0 or 1).</summary>
    public RectangleF Half(int side)
    {
        float w = MathF.Floor(this.Bounds.Width * 0.5f);
        return side == 0
            ? new RectangleF(this.Bounds.X, this.Bounds.Y, w, this.Bounds.Height)
            : new RectangleF(this.Bounds.X + w, this.Bounds.Y, this.Bounds.Width - w, this.Bounds.Height);
    }

    /// <summary>Flips to the other side; returns whether it changed.</summary>
    public bool Flip()
    {
        if (!this.IsEnabled)
        {
            return false;
        }

        this.Selected = 1 - this.Selected;
        return true;
    }

    /// <summary>Tracks the pointer; a click on the other half selects it. Returns whether the choice changed.</summary>
    public bool Update(Vector2 mousePos, bool leftPressed, float dt)
    {
        _time += Math.Max(0f, dt);
        this.Hovered = !this.IsEnabled ? -1 : this.Half(0).Contains(mousePos) ? 0 : this.Half(1).Contains(mousePos) ? 1 : -1;
        if (leftPressed && this.Hovered >= 0 && this.Hovered != this.Selected)
        {
            this.Selected = this.Hovered;
            return true;
        }

        return false;
    }

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        for (int side = 0; side < 2; side++)
        {
            RectangleF half = this.Half(side);
            bool chosen = side == this.Selected;
            ColorRgba fill = chosen ? new ColorRgba(84, 70, 44, 245) : side == this.Hovered ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill;
            ColorRgba frame = chosen ? CarbonUiPalette.BorderActive : CarbonUiPalette.BorderSubtle;
            skin.DrawButton(spriteBatch, in half, this.IsEnabled ? fill : MenuPalette.DisabledFill, this.IsEnabled ? frame : MenuPalette.DisabledBorder);
        }
    }

    public void DrawOverlay(GeometryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (this.IsFocused && this.IsEnabled)
        {
            MenuButton.DrawFocusRing(batch, this.Bounds, _time);
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        for (int side = 0; side < 2; side++)
        {
            RectangleF half = this.Half(side);
            string label = CarbonUiSkin.TruncateToWidth(font, side == 0 ? this.First : this.Second, half.Width - 12f, 1f);
            TextMeasureResult m = font.MeasureString(label, TextLayoutOptions.Default, 1f);
            ColorRgba color = !this.IsEnabled ? MenuPalette.DisabledText : side == this.Selected ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextMuted;
            spriteBatch.DrawOutlinedString(font, label, new Vector2(MathF.Floor(half.X + ((half.Width - m.Width) * 0.5f)), MathF.Floor(half.Y + ((half.Height - m.Height) * 0.5f))), color, CarbonUiPalette.TextOutline, 1, 1f);
        }
    }
}
