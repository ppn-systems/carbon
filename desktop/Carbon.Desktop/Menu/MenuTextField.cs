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

namespace Carbon.Desktop.Menu;

/// <summary>The kind of line shown under a field.</summary>
internal enum FieldFeedback
{
    /// <summary>Nothing under the field.</summary>
    None,

    /// <summary>A problem to fix: red text, a "!" badge and a red border.</summary>
    Error,

    /// <summary>Correct: a green check inside the field; the message (if any) is green.</summary>
    Success,

    /// <summary>A neutral note (e.g. the password strength word).</summary>
    Hint,
}

/// <summary>
/// A single-line menu text field: label above, optional icon at the left, placeholder, blinking caret, inline feedback
/// under the field, a caps-lock warning, and, for passwords, a show/hide eye. The text only ever lives in this field's
/// buffer: revealing changes what is drawn, never what is stored, logged or sent.
/// </summary>
internal sealed class MenuTextField
{
    /// <summary>Height of the feedback line under the field; layouts reserve it.</summary>
    public const float FeedbackHeight = 20f;

    private const float IconWidth = 30f;
    private const float EyeWidth = 32f;
    private const char MaskChar = '*';

    private readonly StringBuilder _buffer = new(128);
    private float _cursorTimer;
    private bool _cursorVisible = true;
    private float _time;
    private int _revision;
    private string _plain = string.Empty;
    private int _plainRevision = -1;
    private string _clipped = string.Empty;
    private int _clipRevision = -1;
    private bool _clipFocused;
    private bool _clipRevealed;
    private float _clipWidth;

    public MenuTextField(string label, string placeholder, string initialValue = "", bool isPassword = false, int maxLength = 64)
    {
        this.Label = label;
        this.Placeholder = placeholder;
        this.IsPassword = isPassword;
        this.MaxLength = maxLength;
        if (!string.IsNullOrEmpty(initialValue))
        {
            _ = _buffer.Append(initialValue.Length <= maxLength ? initialValue : initialValue[..maxLength]);
        }
    }

    public string Label { get; }

    public string Placeholder { get; }

    public bool IsPassword { get; set; }

    public int MaxLength { get; }

    public RectangleF Bounds { get; set; }

    public bool IsFocused { get; set; }

    /// <summary>Gets whether the pointer is over the field.</summary>
    public bool IsHovered { get; private set; }

    public Func<char, bool>? CharacterFilter { get; init; }

    /// <summary>Gets the icon drawn at the left edge.</summary>
    public MenuFieldIcon Icon { get; init; }

    /// <summary>Gets or sets whether a password field shows its text instead of masks.</summary>
    public bool IsRevealed { get; set; }

    /// <summary>Gets or sets whether the caps-lock warning may show (the scene sets it from <see cref="CapsLockTracker"/>).</summary>
    public bool CapsLockOn { get; set; }

    /// <summary>Gets or sets the caps-lock warning text.</summary>
    public string CapsLockMessage { get; set; } = string.Empty;

    /// <summary>Gets the kind of line under the field.</summary>
    public FieldFeedback Feedback { get; private set; }

    /// <summary>Gets the line under the field.</summary>
    public string FeedbackMessage { get; private set; } = string.Empty;

    /// <summary>Gets a counter that changes whenever the text changes.</summary>
    public int Revision => _revision;

    /// <summary>Gets the length of the text without copying it.</summary>
    public int Length => _buffer.Length;

    /// <summary>Gets whether the caps-lock warning is drawn this frame (focused password field with caps lock on).</summary>
    public bool ShowsCapsLockWarning => this.CapsLockOn && this.IsFocused && this.IsPassword;

    /// <summary>Gets the clickable show/hide eye of a password field (empty for other fields).</summary>
    public RectangleF RevealBounds => this.IsPassword
        ? new RectangleF(this.Bounds.Right - EyeWidth, this.Bounds.Y, EyeWidth, this.Bounds.Height)
        : default;

    /// <summary>Gets the text as drawn: masks for a hidden password, the text otherwise. Cached until the text changes.</summary>
    public string DisplayText => this.IsPassword && !this.IsRevealed
        ? (_buffer.Length == 0 ? string.Empty : this.Masked())
        : this.Plain();

    public string Text
    {
        get => this.Plain();
        set
        {
            _ = _buffer.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                _ = _buffer.Append(value.Length <= this.MaxLength ? value : value[..this.MaxLength]);
            }

            _revision++;
        }
    }

    /// <summary>Sets the line under the field; <see cref="FieldFeedback.None"/> clears it.</summary>
    public void SetFeedback(FieldFeedback kind, string message)
    {
        this.Feedback = kind;
        this.FeedbackMessage = kind == FieldFeedback.None ? string.Empty : message ?? string.Empty;
    }

    /// <summary>Flips the password between shown and masked. The stored text is unchanged.</summary>
    public void ToggleReveal()
    {
        if (this.IsPassword)
        {
            this.IsRevealed = !this.IsRevealed;
        }
    }

    public void Update(float dt, Vector2 mousePos, bool leftPressed)
    {
        _time += Math.Max(0f, dt);
        this.IsHovered = this.Bounds.Contains(mousePos);
        if (leftPressed)
        {
            this.IsFocused = this.IsHovered;
            _cursorTimer = 0f;
            _cursorVisible = true;
        }

        if (this.IsFocused)
        {
            _cursorTimer += dt;
            if (_cursorTimer >= 0.5f)
            {
                _cursorTimer = 0f;
                _cursorVisible = !_cursorVisible;
            }
        }
    }

    /// <summary>Types <paramref name="ch"/> into a focused field.</summary>
    /// <returns><see langword="false"/> when the field's character filter refused a printable character (the scene can explain why).</returns>
    public bool AppendChar(char ch)
    {
        if (!this.IsFocused || char.IsControl(ch) || _buffer.Length >= this.MaxLength)
        {
            return true;
        }

        if (this.CharacterFilter is not null && !this.CharacterFilter(ch))
        {
            return false;
        }

        _ = _buffer.Append(ch);
        _revision++;
        _cursorTimer = 0f;
        _cursorVisible = true;
        return true;
    }

    public void Backspace()
    {
        if (!this.IsFocused || _buffer.Length == 0)
        {
            return;
        }

        _ = _buffer.Remove(_buffer.Length - 1, 1);
        _revision++;
        _cursorTimer = 0f;
        _cursorVisible = true;
    }

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin)
    {
        ArgumentNullException.ThrowIfNull(skin);
        ColorRgba fill = this.IsFocused ? CarbonUiPalette.SlotSelectedFill : this.IsHovered ? CarbonUiPalette.SlotHoverFill : CarbonUiPalette.SlotFill;
        ColorRgba border = this.Feedback == FieldFeedback.Error ? MenuPalette.ErrorText
            : this.IsFocused ? CarbonUiPalette.BorderActive
            : this.IsHovered ? MenuPalette.HoverBorder
            : CarbonUiPalette.BorderSubtle;
        skin.DrawInset(spriteBatch, this.Bounds, fill, border);
    }

    /// <summary>Draws the vector parts: focus ring, icon, caret, eye, check mark and feedback badge.</summary>
    public void DrawCursorGeometry(GeometryBatch batch, BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(font);
        RectangleF b = this.Bounds;
        if (b.Width <= 0f)
        {
            return;
        }

        float midY = b.Y + (b.Height * 0.5f);
        if (this.IsFocused)
        {
            MenuButton.DrawFocusRing(batch, in b, _time);
        }

        if (this.Icon != MenuFieldIcon.None)
        {
            MenuIcons.DrawField(batch, this.Icon, new Vector2(b.X + 16f, midY), this.IsFocused ? CarbonUiPalette.BorderActive : MenuPalette.Icon);
        }

        if (this.IsPassword)
        {
            RectangleF eye = this.RevealBounds;
            MenuIcons.DrawEye(batch, new Vector2(eye.X + (eye.Width * 0.5f), midY), this.IsRevealed, this.IsRevealed ? CarbonUiPalette.BorderActive : MenuPalette.Icon);
        }

        if (this.Feedback == FieldFeedback.Success)
        {
            float checkX = b.Right - (this.IsPassword ? EyeWidth : 0f) - 16f;
            MenuIcons.DrawCheck(batch, new Vector2(checkX, midY), MenuPalette.SuccessText);
        }

        if (this.IsFocused && _cursorVisible)
        {
            string display = this.ClippedDisplay(font);
            TextMeasureResult size = font.MeasureString(display, TextLayoutOptions.Default, 1f);
            float cursorX = MathF.Min(this.TextRight() + 2f, this.TextLeft() + size.Width + 2f);
            batch.FillRectangle(new RectangleF(cursorX, b.Y + 9f, 2f, b.Height - 18f), CarbonUiPalette.BorderActive);
        }

        Vector2 badge = new(b.X + 6f, b.Bottom + 4f + (FeedbackHeight * 0.5f) - 2f);
        if (this.ShowsCapsLockWarning)
        {
            MenuIcons.DrawCapsLock(batch, badge, MenuPalette.WarningText);
        }
        else if (this.Feedback == FieldFeedback.Error && this.FeedbackMessage.Length > 0)
        {
            MenuIcons.DrawAlert(batch, badge, MenuPalette.ErrorText);
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font)
    {
        ArgumentNullException.ThrowIfNull(font);
        ColorRgba black = CarbonUiPalette.TextOutline;
        RectangleF b = this.Bounds;
        spriteBatch.DrawOutlinedString(font, this.Label, new Vector2(b.X, b.Y - 23f), this.IsFocused ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextMuted, black, 1, 1f);

        bool empty = _buffer.Length == 0;
        string display = empty ? CarbonUiSkin.TruncateToWidth(font, this.Placeholder, this.TextRight() - this.TextLeft(), 1f) : this.ClippedDisplay(font);
        ColorRgba textColor = empty ? MenuPalette.Placeholder : CarbonUiPalette.TextPrimary;
        float ty = b.Y + MathF.Floor((b.Height - font.LineHeight) * 0.5f);
        spriteBatch.DrawOutlinedString(font, display, new Vector2(this.TextLeft(), ty), textColor, black, 1, 1f);

        (string line, ColorRgba color) = this.ShowsCapsLockWarning
            ? (this.CapsLockMessage, MenuPalette.WarningText)
            : (this.FeedbackMessage, this.Feedback switch
            {
                FieldFeedback.Error => MenuPalette.ErrorText,
                FieldFeedback.Success => MenuPalette.SuccessText,
                FieldFeedback.None or FieldFeedback.Hint => CarbonUiPalette.TextMuted,
                _ => CarbonUiPalette.TextMuted,
            });
        if (line.Length > 0)
        {
            bool badged = this.ShowsCapsLockWarning || this.Feedback == FieldFeedback.Error;
            float lx = b.X + (badged ? 18f : 0f);
            string fitted = CarbonUiSkin.TruncateToWidth(font, line, b.Right - lx, 1f);
            spriteBatch.DrawOutlinedString(font, fitted, new Vector2(lx, b.Bottom + 4f), color, black, 1, 1f);
        }
    }

    private float TextLeft() => this.Bounds.X + (this.Icon == MenuFieldIcon.None ? 12f : IconWidth + 2f);

    private float TextRight() =>
        this.Bounds.Right - 10f - (this.IsPassword ? EyeWidth : 0f) - (this.Feedback == FieldFeedback.Success ? 24f : 0f);

    private string Plain()
    {
        if (_plainRevision != _revision)
        {
            _plain = _buffer.ToString();
            _plainRevision = _revision;
        }

        return _plain;
    }

    private string Masked() => new(MaskChar, _buffer.Length);

    /// <summary>The display text fitted to the field: the tail while typing, the head with an ellipsis otherwise. Cached.</summary>
    private string ClippedDisplay(BitmapFont font)
    {
        float maxWidth = MathF.Max(20f, this.TextRight() - this.TextLeft());
        if (_clipRevision == _revision && _clipFocused == this.IsFocused && _clipRevealed == this.IsRevealed && _clipWidth == maxWidth)
        {
            return _clipped;
        }

        _clipRevision = _revision;
        _clipFocused = this.IsFocused;
        _clipRevealed = this.IsRevealed;
        _clipWidth = maxWidth;
        _clipped = Fit(font, this.DisplayText, maxWidth, keepTail: this.IsFocused);
        return _clipped;
    }

    private static string Fit(BitmapFont font, string raw, float maxWidth, bool keepTail)
    {
        if (raw.Length == 0 || font.MeasureString(raw, TextLayoutOptions.Default, 1f).Width <= maxWidth)
        {
            return raw;
        }

        if (keepTail)
        {
            for (int start = 1; start < raw.Length; start++)
            {
                if (font.MeasureString(raw.AsSpan(start), TextLayoutOptions.Default, 1f).Width <= maxWidth)
                {
                    return raw[start..];
                }
            }

            return raw[^1..];
        }

        return CarbonUiSkin.TruncateToWidth(font, raw, maxWidth, 1f);
    }
}
