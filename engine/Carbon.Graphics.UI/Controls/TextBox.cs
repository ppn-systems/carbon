// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers;
using System.Numerics;
using System.Text;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.UI.Common;
using Carbon.Graphics.UI.NineSlice;

namespace Carbon.Graphics.UI.Controls;

/// <summary>
/// Represents an interactive single-line text input control for chat, login, and player input.
/// Supports cursor blinking, backspace, character typing, and selection.
/// </summary>
public sealed class TextBox : IFocusable
{
    private readonly StringBuilder _buffer = new(128);
    private float _cursorTimer;
    private bool _cursorVisible = true;
    private int _cursorIndex;

    /// <summary>
    /// Occurs when the user submits text (presses Enter).
    /// </summary>
    public event Action<TextBox, string>? Submitted;

    /// <summary>
    /// Gets or sets the screen bounds of this text box.
    /// </summary>
    public RectangleF Bounds { get; set; }

    /// <summary>
    /// Gets or sets the text content of this input control.
    /// </summary>
    public string Text
    {
        get => _buffer.ToString();
        set
        {
            _ = _buffer.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                _ = _buffer.Append(value);
            }

            _cursorIndex = _buffer.Length;
        }
    }

    /// <summary>
    /// Gets or sets the placeholder text shown when the input buffer is empty.
    /// </summary>
    public string Placeholder { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of characters permitted in this text box.
    /// </summary>
    public int MaxLength { get; set; } = 128;

    /// <summary>
    /// Gets or sets a value indicating whether this control currently holds keyboard focus.
    /// </summary>
    public bool IsFocused { get; set; }

    /// <inheritdoc/>
    public bool IsFocusable => true;

    /// <inheritdoc/>
    public int TabIndex { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether text input characters are masked for password entry.
    /// </summary>
    public bool IsPassword { get; set; }

    /// <summary>
    /// Gets or sets the masking character used when <see cref="IsPassword"/> is true (default is '•').
    /// </summary>
    public char PasswordChar { get; set; } = '•';

    /// <inheritdoc/>
    public void OnFocusGained()
    {
        _cursorVisible = true;
        _cursorTimer = 0f;
    }

    /// <inheritdoc/>
    public void OnFocusLost() =>
        _cursorVisible = false;

    /// <summary>
    /// Gets or sets the font asset.
    /// </summary>
    public BitmapFont? Font { get; set; }

    /// <summary>
    /// Gets or sets the background skin texture.
    /// </summary>
    public ITexture? BackgroundTexture { get; set; }

    /// <summary>
    /// Gets or sets the background texture bounds.
    /// </summary>
    public RectangleF BackgroundSource { get; set; }

    /// <summary>
    /// Gets or sets the background nine-slice margins.
    /// </summary>
    public NineSliceMargins BackgroundMargins { get; set; }

    /// <summary>
    /// Gets or sets the interior text padding.
    /// </summary>
    public Padding Padding { get; set; } = new(6f, 4f);

    /// <summary>
    /// Gets or sets the active text color.
    /// </summary>
    public ColorRgba TextColor { get; set; } = ColorRgba.White;

    /// <summary>
    /// Gets or sets the placeholder text color.
    /// </summary>
    public ColorRgba PlaceholderColor { get; set; } = new(160, 160, 160, 200);

    /// <summary>
    /// Gets or sets the blinking cursor color.
    /// </summary>
    public ColorRgba CursorColor { get; set; } = ColorRgba.White;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextBox"/> class.
    /// </summary>
    /// <param name="bounds">Screen bounds.</param>
    /// <param name="placeholder">Placeholder hint text.</param>
    /// <param name="font">Font asset.</param>
    public TextBox(in RectangleF bounds, string placeholder = "", BitmapFont? font = null)
    {
        this.Bounds = bounds;
        this.Placeholder = placeholder ?? string.Empty;
        this.Font = font;
    }

    /// <summary>
    /// Appends typed character into the text buffer at the cursor position.
    /// </summary>
    /// <param name="c">The character typed.</param>
    public void AppendChar(char c)
    {
        if (!this.IsFocused || char.IsControl(c) || _buffer.Length >= this.MaxLength)
        {
            return;
        }

        _ = _buffer.Insert(_cursorIndex, c);
        _cursorIndex++;
        _cursorVisible = true;
        _cursorTimer = 0f;
    }

    /// <summary>
    /// Handles the backspace key action, removing character to the left of the cursor.
    /// </summary>
    public void HandleBackspace()
    {
        if (!this.IsFocused || _cursorIndex <= 0 || _buffer.Length == 0)
        {
            return;
        }

        _ = _buffer.Remove(_cursorIndex - 1, 1);
        _cursorIndex--;
        _cursorVisible = true;
        _cursorTimer = 0f;
    }

    /// <summary>
    /// Handles the submit action (Enter key), dispatching <see cref="Submitted"/>.
    /// </summary>
    public void HandleSubmit()
    {
        if (this.IsFocused)
        {
            this.Submitted?.Invoke(this, this.Text);
        }
    }

    /// <summary>
    /// Updates blinking cursor animation and handles focus toggling via mouse click.
    /// </summary>
    /// <param name="deltaTime">Elapsed time in seconds.</param>
    /// <param name="mousePosition">Mouse cursor position.</param>
    /// <param name="isMouseDown">Whether the primary mouse button was clicked.</param>
    public void Update(float deltaTime, Vector2 mousePosition, bool isMouseDown)
    {
        if (isMouseDown)
        {
            this.IsFocused = this.Bounds.Contains(mousePosition);
            _cursorVisible = true;
            _cursorTimer = 0f;
        }

        if (this.IsFocused)
        {
            _cursorTimer += deltaTime;
            if (_cursorTimer >= 0.5f)
            {
                _cursorTimer = 0f;
                _cursorVisible = !_cursorVisible;
            }
        }
    }

    /// <summary>
    /// Renders the text box, interior text, placeholder, and blinking cursor into the sprite batch.
    /// </summary>
    /// <param name="spriteBatch">Target sprite batch.</param>
    /// <param name="depth">Depth buffer value.</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting.</param>
    /// <param name="layer">Rendering layer index.</param>
    public void Draw(
        SpriteBatch spriteBatch,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));

        float finalFootY = footY == 0f ? this.Bounds.Bottom : footY;

        // 1. Draw Nine-Slice Background
        if (this.BackgroundTexture != null && this.BackgroundSource.Width > 0f)
        {
            spriteBatch.DrawNineSlice(
                this.BackgroundTexture,
                this.BackgroundSource,
                this.BackgroundMargins,
                this.Bounds,
                ColorRgba.White,
                depth,
                finalFootY,
                layer);
        }

        // 2. Draw Text or Placeholder
        if (this.Font != null)
        {
            float contentX = this.Bounds.X + this.Padding.Left;
            float contentY = this.Bounds.Y + this.Padding.Top;
            Vector2 textPos = new(contentX, contentY);

            if (_buffer.Length == 0 && !string.IsNullOrEmpty(this.Placeholder))
            {
                spriteBatch.DrawString(
                    this.Font,
                    this.Placeholder.AsSpan(),
                    textPos,
                    this.PlaceholderColor,
                    scale: 1.0f,
                    depth: depth,
                    footY: finalFootY,
                    layer: layer);
            }
            else if (_buffer.Length > 0)
            {
                Span<char> scratch = stackalloc char[256];
                char[]? rented = null;
                Span<char> displayChars = _buffer.Length <= scratch.Length
                    ? scratch[.._buffer.Length]
                    : (rented = ArrayPool<char>.Shared.Rent(_buffer.Length)).AsSpan(0, _buffer.Length);

                try
                {
                    if (this.IsPassword)
                    {
                        displayChars.Fill(this.PasswordChar);
                    }
                    else
                    {
                        _buffer.CopyTo(0, displayChars, _buffer.Length);
                    }

                    spriteBatch.DrawString(
                        this.Font,
                        displayChars,
                        textPos,
                        this.TextColor,
                        scale: 1.0f,
                        depth: depth,
                        footY: finalFootY,
                        layer: layer);

                    // 3. Draw Blinking Cursor
                    if (this.IsFocused && _cursorVisible)
                    {
                        int safeCursor = Math.Clamp(_cursorIndex, 0, _buffer.Length);
                        ReadOnlySpan<char> prefix = displayChars[..safeCursor];
                        TextMeasureResult prefixMeasure = this.Font.MeasureString(prefix);

                        float cursorX = contentX + prefixMeasure.Width;
                        float cursorHeight = this.Font.LineHeight;

                        if (this.BackgroundTexture != null)
                        {
                            // Render 2px width cursor bar
                            spriteBatch.Draw(
                                this.BackgroundTexture,
                                new Vector2(cursorX, contentY),
                                new RectangleF(this.BackgroundSource.X, this.BackgroundSource.Y, 2f, cursorHeight),
                                this.CursorColor,
                                rotation: 0f,
                                origin: Vector2.Zero,
                                scale: Vector2.One,
                                effects: SpriteEffects.None,
                                depth: depth,
                                footY: finalFootY,
                                layer: layer);
                        }
                    }
                }
                finally
                {
                    if (rented != null)
                    {
                        ArrayPool<char>.Shared.Return(rented);
                    }
                }
            }

            // Draw cursor when buffer is empty
            if (_buffer.Length == 0 && this.IsFocused && _cursorVisible && this.BackgroundTexture != null)
            {
                spriteBatch.Draw(
                    this.BackgroundTexture,
                    new Vector2(contentX, contentY),
                    new RectangleF(this.BackgroundSource.X, this.BackgroundSource.Y, 2f, this.Font.LineHeight),
                    this.CursorColor,
                    rotation: 0f,
                    origin: Vector2.Zero,
                    scale: Vector2.One,
                    effects: SpriteEffects.None,
                    depth: depth,
                    footY: finalFootY,
                    layer: layer);
            }
        }
    }
}
