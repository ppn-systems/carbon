// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using System.Text;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Scenes;

internal enum MenuButtonStyle
{
    PrimaryGold,
    SecondaryEmerald,
    NeutralSlate,
    DangerRed,
}

internal sealed class MenuTextField
{
    private readonly StringBuilder _buffer = new(128);
    private float _cursorTimer;
    private bool _cursorVisible = true;

    public MenuTextField(string label, string placeholder, string initialValue = "", bool isPassword = false, int maxLength = 64)
    {
        this.Label = label;
        this.Placeholder = placeholder;
        this.IsPassword = isPassword;
        this.MaxLength = maxLength;
        if (!string.IsNullOrEmpty(initialValue))
        {
            _ = _buffer.Append(initialValue);
        }
    }

    public string Label { get; }

    public string Placeholder { get; }

    public bool IsPassword { get; set; }

    public int MaxLength { get; }

    public RectangleF Bounds { get; set; }

    public bool IsFocused { get; set; }

    public Func<char, bool>? CharacterFilter { get; init; }

    public string Text
    {
        get => _buffer.ToString();
        set
        {
            _ = _buffer.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                _ = _buffer.Append(value.Length <= this.MaxLength ? value : value[..this.MaxLength]);
            }
        }
    }

    public void Update(float dt, Vector2 mousePos, bool leftPressed)
    {
        if (leftPressed)
        {
            this.IsFocused = this.Bounds.Contains(mousePos);
            _cursorTimer = 0f;
            _cursorVisible = true;
        }

        if (this.IsFocused)
        {
            _cursorTimer += dt;
            if (_cursorTimer >= 0.45f)
            {
                _cursorTimer = 0f;
                _cursorVisible = !_cursorVisible;
            }
        }
    }

    public void AppendChar(char ch)
    {
        if (!this.IsFocused || char.IsControl(ch) || _buffer.Length >= this.MaxLength)
        {
            return;
        }

        if (this.CharacterFilter is not null && !this.CharacterFilter(ch))
        {
            return;
        }

        _ = _buffer.Append(ch);
        _cursorTimer = 0f;
        _cursorVisible = true;
    }

    public void Backspace()
    {
        if (!this.IsFocused || _buffer.Length == 0)
        {
            return;
        }

        _ = _buffer.Remove(_buffer.Length - 1, 1);
        _cursorTimer = 0f;
        _cursorVisible = true;
    }

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        ColorRgba fill = this.IsFocused
            ? new ColorRgba(24, 34, 58, 245)
            : new ColorRgba(12, 18, 34, 232);
        ColorRgba border = this.IsFocused
            ? new ColorRgba(250, 204, 21, 255)
            : new ColorRgba(71, 85, 105, 225);
        skin.DrawInset(spriteBatch, this.Bounds, fill, border);
    }

    public void DrawCursorGeometry(GeometryBatch batch, BitmapFont font)
    {
        if (this.IsFocused && _cursorVisible)
        {
            string display = this.GetClippedDisplayText(font, MathF.Max(20f, this.Bounds.Width - 24f));
            TextMeasureResult size = font.MeasureString(display, TextLayoutOptions.Default, 2f);
            float cursorX = MathF.Min(this.Bounds.Right - 10f, this.Bounds.X + 12f + size.Width + 2f);
            float cursorY = this.Bounds.Y + 9f;
            batch.FillRectangle(new RectangleF(cursorX, cursorY, 3f, this.Bounds.Height - 18f), new ColorRgba(250, 204, 21, 255));
        }
    }

    public void DrawGeometry(GeometryBatch batch, BitmapFont font)
    {
        ColorRgba bg = this.IsFocused
            ? new ColorRgba(24, 34, 58, 245)
            : new ColorRgba(15, 23, 42, 230);
        ColorRgba border = this.IsFocused
            ? new ColorRgba(250, 204, 21, 255)
            : new ColorRgba(71, 85, 105, 220);

        batch.FillRectangle(this.Bounds, bg);
        batch.DrawRectangle(this.Bounds, border, this.IsFocused ? 2f : 1.5f);

        if (this.IsFocused && _cursorVisible)
        {
            string display = this.GetClippedDisplayText(font, MathF.Max(20f, this.Bounds.Width - 24f));
            TextMeasureResult size = font.MeasureString(display, TextLayoutOptions.Default, 2f);
            float cursorX = MathF.Min(this.Bounds.Right - 10f, this.Bounds.X + 12f + size.Width + 2f);
            float cursorY = this.Bounds.Y + 9f;
            batch.FillRectangle(new RectangleF(cursorX, cursorY, 3f, this.Bounds.Height - 18f), new ColorRgba(250, 204, 21, 255));
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font)
    {
        ColorRgba black = new(8, 10, 16, 255);
        spriteBatch.DrawOutlinedString(
            font,
            this.Label,
            new Vector2(this.Bounds.X, this.Bounds.Y - 22f),
            new ColorRgba(203, 213, 225, 255),
            black,
            1,
            2f);

        float maxTextW = MathF.Max(20f, this.Bounds.Width - 24f);
        string display = _buffer.Length == 0 ? this.Placeholder : this.GetClippedDisplayText(font, maxTextW);
        ColorRgba textColor = _buffer.Length == 0
            ? new ColorRgba(100, 116, 139, 255)
            : ColorRgba.White;

        spriteBatch.DrawOutlinedString(
            font,
            display,
            new Vector2(this.Bounds.X + 12f, this.Bounds.Y + MathF.Floor((this.Bounds.Height - 16f) * 0.5f)),
            textColor,
            black,
            1,
            2f);
    }

    private string GetClippedDisplayText(BitmapFont font, float maxPixelWidth)
    {
        if (_buffer.Length == 0)
        {
            return string.Empty;
        }

        string raw = this.IsPassword ? new string('*', _buffer.Length) : _buffer.ToString();
        if (font.MeasureString(raw, TextLayoutOptions.Default, 2f).Width <= maxPixelWidth)
        {
            return raw;
        }

        if (this.IsFocused)
        {
            for (int start = 1; start < raw.Length; start++)
            {
                string tail = raw[start..];
                if (font.MeasureString(tail, TextLayoutOptions.Default, 2f).Width <= maxPixelWidth)
                {
                    return tail;
                }
            }

            return raw[^1..];
        }

        for (int len = raw.Length - 1; len > 1; len--)
        {
            string head = $"{raw[..len]}...";
            if (font.MeasureString(head, TextLayoutOptions.Default, 2f).Width <= maxPixelWidth)
            {
                return head;
            }
        }

        return "...";
    }
}

internal sealed class MenuButton
{
    public MenuButton(string text, MenuButtonStyle style = MenuButtonStyle.NeutralSlate)
    {
        this.Text = text;
        this.Style = style;
    }

    public RectangleF Bounds { get; set; }

    public string Text { get; set; }

    public MenuButtonStyle Style { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool IsHovered { get; private set; }

    public bool IsPressed { get; private set; }

    public bool Update(Vector2 mousePos, bool leftDown, bool leftPressed)
    {
        if (!this.IsEnabled)
        {
            this.IsHovered = false;
            this.IsPressed = false;
            return false;
        }

        this.IsHovered = this.Bounds.Contains(mousePos);
        this.IsPressed = this.IsHovered && leftDown;
        return this.IsHovered && leftPressed;
    }

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        (ColorRgba fill, ColorRgba border) = this.ResolveColors();
        skin.DrawButton(spriteBatch, this.Bounds, fill, border);
    }

    public void DrawGeometry(GeometryBatch batch)
    {
        (ColorRgba fill, ColorRgba border) = this.ResolveColors();
        batch.FillRectangle(this.Bounds, fill);
        batch.DrawRectangle(this.Bounds, border, this.IsHovered ? 2f : 1.5f);
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font)
    {
        ColorRgba black = new(8, 10, 16, 255);
        ColorRgba textColor = this.IsEnabled ? ColorRgba.White : new ColorRgba(148, 163, 184, 220);
        TextMeasureResult measure = font.MeasureString(this.Text, TextLayoutOptions.Default, 2f);
        float tx = MathF.Floor(this.Bounds.X + ((this.Bounds.Width - measure.Width) * 0.5f));
        float ty = MathF.Floor(this.Bounds.Y + ((this.Bounds.Height - measure.Height) * 0.5f)) + (this.IsPressed ? 1f : 0f);
        spriteBatch.DrawOutlinedString(font, this.Text, new Vector2(tx, ty), textColor, black, 1, 2f);
    }

    private (ColorRgba Fill, ColorRgba Border) ResolveColors()
    {
        if (!this.IsEnabled)
        {
            return (new ColorRgba(30, 41, 59, 175), new ColorRgba(71, 85, 105, 160));
        }

        if (this.Style == MenuButtonStyle.PrimaryGold)
        {
            return this.IsPressed
                ? (new ColorRgba(180, 83, 9, 245), new ColorRgba(253, 224, 71, 255))
                : (this.IsHovered
                    ? (new ColorRgba(217, 119, 6, 245), new ColorRgba(253, 224, 71, 255))
                    : (new ColorRgba(161, 98, 7, 235), new ColorRgba(250, 204, 21, 240)));
        }

        if (this.Style == MenuButtonStyle.SecondaryEmerald)
        {
            return this.IsPressed
                ? (new ColorRgba(4, 120, 87, 245), new ColorRgba(110, 231, 183, 255))
                : (this.IsHovered
                    ? (new ColorRgba(16, 185, 129, 240), new ColorRgba(167, 243, 208, 255))
                    : (new ColorRgba(5, 150, 105, 230), new ColorRgba(52, 211, 153, 235)));
        }

        if (this.Style == MenuButtonStyle.DangerRed)
        {
            return this.IsPressed
                ? (new ColorRgba(153, 27, 27, 245), new ColorRgba(252, 165, 165, 255))
                : (this.IsHovered
                    ? (new ColorRgba(220, 38, 38, 240), new ColorRgba(254, 202, 202, 255))
                    : (new ColorRgba(185, 28, 28, 225), new ColorRgba(248, 113, 113, 235)));
        }

        return this.IsPressed
            ? (new ColorRgba(51, 65, 85, 245), new ColorRgba(203, 213, 225, 255))
            : (this.IsHovered
                ? (new ColorRgba(71, 85, 105, 240), new ColorRgba(226, 232, 240, 255))
                : (new ColorRgba(30, 41, 59, 230), new ColorRgba(100, 116, 139, 225)));
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
