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

/// <summary>Colour family of a menu button.</summary>
internal enum MenuButtonStyle
{
    PrimaryGold,
    SecondaryEmerald,
    NeutralSlate,
    DangerRed,
}

/// <summary>What a button looks like this frame (docs/art/menu-ui.md, "Button states").</summary>
internal enum MenuButtonVisualState
{
    Normal,
    Hovered,
    Pressed,
    Focused,
    Disabled,
    Busy,
}

/// <summary>
/// A menu button with every state of the shared visual language: normal, hover, pressed, keyboard focus (a 2 px ring
/// outside the frame), disabled and busy (a spinner). Hover and press ease in over about 120 ms and 60 ms; with no
/// frame time they snap, so tests and static captures see the end state.
/// </summary>
internal sealed class MenuButton
{
    /// <summary>Seconds a hover fade takes.</summary>
    public const float HoverSeconds = 0.12f;

    /// <summary>Seconds a press fade takes.</summary>
    public const float PressSeconds = 0.06f;

    private float _time;

    public MenuButton(string text, MenuButtonStyle style = MenuButtonStyle.NeutralSlate)
    {
        this.Text = text;
        this.Style = style;
    }

    public RectangleF Bounds { get; set; }

    public string Text { get; set; }

    public MenuButtonStyle Style { get; set; }

    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets whether the button's action is running (spinner, no clicks).</summary>
    public bool IsBusy { get; set; }

    public bool IsHovered { get; private set; }

    public bool IsPressed { get; private set; }

    /// <summary>Gets or sets whether keyboard focus is on the button (drawn with the focus ring).</summary>
    public bool IsFocused { get; set; }

    /// <summary>Gets the eased hover amount, 0 (rest) to 1 (fully lit).</summary>
    public float HotAmount { get; private set; }

    /// <summary>Gets the eased press amount, 0 (up) to 1 (fully down).</summary>
    public float PressAmount { get; private set; }

    /// <summary>Gets the state the button draws this frame; busy and disabled win over pointer and focus.</summary>
    public MenuButtonVisualState State =>
        this.IsBusy ? MenuButtonVisualState.Busy
        : !this.IsEnabled ? MenuButtonVisualState.Disabled
        : this.IsPressed ? MenuButtonVisualState.Pressed
        : this.IsHovered ? MenuButtonVisualState.Hovered
        : this.IsFocused ? MenuButtonVisualState.Focused
        : MenuButtonVisualState.Normal;

    /// <summary>
    /// Tracks the pointer; returns whether the button was clicked this frame. <paramref name="dt"/> advances the hover
    /// and press easing (0 snaps to the end state).
    /// </summary>
    public bool Update(Vector2 mousePos, bool leftDown, bool leftPressed, float dt = 0f)
    {
        _time += Math.Max(0f, dt);
        bool interactive = this.IsEnabled && !this.IsBusy;
        this.IsHovered = interactive && this.Bounds.Contains(mousePos);
        this.IsPressed = this.IsHovered && leftDown;
        this.HotAmount = Approach(this.HotAmount, this.IsHovered || this.IsFocused ? 1f : 0f, dt, HoverSeconds);
        this.PressAmount = Approach(this.PressAmount, this.IsPressed ? 1f : 0f, dt, PressSeconds);
        return this.IsHovered && leftPressed;
    }

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        (ColorRgba fill, ColorRgba border) = this.ResolveColors();
        RectangleF bounds = this.ContentBounds();
        skin.DrawButton(spriteBatch, in bounds, fill, border);
    }

    /// <summary>Draws the bevel highlight, the focus ring and the busy spinner (vector pass, after the skins).</summary>
    public void DrawOverlay(GeometryBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (this.Bounds.Width <= 0f)
        {
            return;
        }

        RectangleF b = this.ContentBounds();
        if (this.IsEnabled && !this.IsBusy)
        {
            // Top bevel: a faint light edge that brightens on hover and disappears when pressed.
            float bevel = (0.10f + (0.16f * this.HotAmount)) * (1f - this.PressAmount);
            batch.FillRectangle(new RectangleF(b.X + 6f, b.Y + 3f, b.Width - 12f, 2f), MenuPalette.WithAlpha(ColorRgba.White, bevel));
        }

        if (this.IsFocused && this.IsEnabled)
        {
            DrawFocusRing(batch, this.Bounds, _time);
        }

        if (this.IsBusy)
        {
            MenuIcons.DrawSpinner(batch, new Vector2(b.X + 22f, b.Y + (b.Height * 0.5f)), 7f, _time, MenuPalette.FocusRing);
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        ColorRgba textColor = !this.IsEnabled || this.IsBusy
            ? MenuPalette.DisabledText
            : MenuPalette.Lerp(CarbonUiPalette.TextPrimary, ColorRgba.White, this.HotAmount);
        RectangleF b = this.ContentBounds();
        TextMeasureResult measure = font.MeasureString(this.Text, TextLayoutOptions.Default, 1f);
        float tx = MathF.Floor(b.X + ((b.Width - measure.Width) * 0.5f));
        float ty = MathF.Floor(b.Y + ((b.Height - measure.Height) * 0.5f));
        spriteBatch.DrawOutlinedString(font, this.Text, new Vector2(tx, ty), textColor, CarbonUiPalette.TextOutline, 1, 1f);
    }

    /// <summary>Draws the shared 2 px keyboard focus ring 3 px outside <paramref name="bounds"/>, with a slow, subtle pulse.</summary>
    public static void DrawFocusRing(GeometryBatch batch, in RectangleF bounds, float time)
    {
        ArgumentNullException.ThrowIfNull(batch);
        float pulse = 0.85f + (0.15f * MathF.Sin(time * 4f));
        RectangleF ring = new(bounds.X - 4f, bounds.Y - 4f, bounds.Width + 8f, bounds.Height + 8f);
        batch.DrawRectangle(in ring, MenuPalette.WithAlpha(MenuPalette.FocusRing, pulse), 2f);
    }

    /// <summary>Moves <paramref name="value"/> toward <paramref name="target"/> at a rate that covers 0→1 in <paramref name="seconds"/>.</summary>
    internal static float Approach(float value, float target, float dt, float seconds)
    {
        if (dt <= 0f)
        {
            return target;
        }

        float step = dt / seconds;
        return value < target ? MathF.Min(target, value + step) : MathF.Max(target, value - step);
    }

    /// <summary>The frame shifted down 1 px while pressed (the label and skin move together).</summary>
    private RectangleF ContentBounds()
    {
        RectangleF b = this.Bounds;
        return new RectangleF(b.X, b.Y + MathF.Round(this.PressAmount), b.Width, b.Height);
    }

    private (ColorRgba Fill, ColorRgba Border) ResolveColors()
    {
        if (!this.IsEnabled || this.IsBusy)
        {
            return (MenuPalette.DisabledFill, MenuPalette.DisabledBorder);
        }

        (ColorRgba rest, ColorRgba hot, ColorRgba down, ColorRgba border) = this.Style switch
        {
            MenuButtonStyle.PrimaryGold => (new ColorRgba(70, 58, 36, 240), new ColorRgba(104, 86, 52, 248), new ColorRgba(56, 46, 30, 248), CarbonUiPalette.BorderActive),
            MenuButtonStyle.SecondaryEmerald => (CarbonUiPalette.ConfirmFill, new ColorRgba(46, 124, 88, 248), new ColorRgba(26, 72, 50, 248), CarbonUiPalette.ConfirmBorder),
            MenuButtonStyle.DangerRed => (CarbonUiPalette.DangerFill, new ColorRgba(156, 46, 52, 248), new ColorRgba(100, 28, 34, 248), CarbonUiPalette.DangerBorder),
            MenuButtonStyle.NeutralSlate => (CarbonUiPalette.CardFill, CarbonUiPalette.SlotHoverFill, CarbonUiPalette.SlotSelectedFill, CarbonUiPalette.BorderSubtle),
            _ => throw new InvalidOperationException($"Unknown button style {this.Style}."),
        };

        ColorRgba fill = MenuPalette.Lerp(MenuPalette.Lerp(rest, hot, this.HotAmount), down, this.PressAmount);
        ColorRgba frame = this.Style == MenuButtonStyle.NeutralSlate
            ? MenuPalette.Lerp(border, CarbonUiPalette.BorderOuter, this.HotAmount)
            : border;
        return (fill, frame);
    }
}
