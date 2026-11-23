// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.UI.Common;
using Carbon.Graphics.UI.NineSlice;

namespace Carbon.Graphics.UI.Controls;

/// <summary>
/// Represents an interactive button control supporting nine-slice skins, font labels, and state transitions.
/// </summary>
public sealed class Button
{
    private ControlState _state = ControlState.Normal;
    private bool _isPressedInternally;

    /// <summary>
    /// Occurs when the button is clicked (pressed and released within boundaries).
    /// </summary>
    public event Action<Button>? Clicked;

    /// <summary>
    /// Gets or sets the screen bounds of this button.
    /// </summary>
    public RectangleF Bounds { get; set; }

    /// <summary>
    /// Gets or sets the text label displayed on the button.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets the current interaction state.
    /// </summary>
    public ControlState State => _state;

    /// <summary>
    /// Gets or sets a value indicating whether the button is interactive.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the font used for rendering the text label.
    /// </summary>
    public BitmapFont? Font { get; set; }

    /// <summary>
    /// Gets or sets the panel skin texture.
    /// </summary>
    public ITexture? Texture { get; set; }

    /// <summary>
    /// Gets or sets the source texture bounds.
    /// </summary>
    public RectangleF SourceBounds { get; set; }

    /// <summary>
    /// Gets or sets the nine-slice border margins.
    /// </summary>
    public NineSliceMargins Margins { get; set; }

    /// <summary>
    /// Gets or sets the text color tint.
    /// </summary>
    public ColorRgba TextColor { get; set; } = ColorRgba.White;

    /// <summary>
    /// Gets or sets the button tint color for normal state.
    /// </summary>
    public ColorRgba NormalTint { get; set; } = ColorRgba.White;

    /// <summary>
    /// Gets or sets the button tint color for hovered state.
    /// </summary>
    public ColorRgba HoverTint { get; set; } = new(230, 230, 230, 255);

    /// <summary>
    /// Gets or sets the button tint color for pressed state.
    /// </summary>
    public ColorRgba PressedTint { get; set; } = new(180, 180, 180, 255);

    /// <summary>
    /// Gets or sets the button tint color for disabled state.
    /// </summary>
    public ColorRgba DisabledTint { get; set; } = new(120, 120, 120, 180);

    /// <summary>
    /// Initializes a new instance of the <see cref="Button"/> class.
    /// </summary>
    /// <param name="bounds">Screen bounds.</param>
    /// <param name="text">Button label text.</param>
    /// <param name="font">Font asset.</param>
    public Button(in RectangleF bounds, string text = "", BitmapFont? font = null)
    {
        this.Bounds = bounds;
        this.Text = text ?? string.Empty;
        this.Font = font;
    }

    /// <summary>
    /// Updates button interaction states given the mouse cursor position and button states.
    /// </summary>
    /// <param name="mousePosition">Current mouse cursor position.</param>
    /// <param name="isMouseDown">Whether the primary mouse button is currently down.</param>
    /// <param name="wasMouseDown">Whether the primary mouse button was down in the previous tick.</param>
    public void Update(Vector2 mousePosition, bool isMouseDown, bool wasMouseDown)
    {
        if (!this.IsEnabled)
        {
            _state = ControlState.Disabled;
            _isPressedInternally = false;
            return;
        }

        bool containsMouse = this.Bounds.Contains(mousePosition);

        if (containsMouse)
        {
            if (isMouseDown)
            {
                if (!wasMouseDown)
                {
                    _isPressedInternally = true;
                }

                _state = _isPressedInternally ? ControlState.Pressed : ControlState.Hovered;
            }
            else
            {
                if (_isPressedInternally && wasMouseDown)
                {
                    this.Clicked?.Invoke(this);
                }

                _isPressedInternally = false;
                _state = ControlState.Hovered;
            }
        }
        else
        {
            if (!isMouseDown)
            {
                _isPressedInternally = false;
            }

            _state = ControlState.Normal;
        }
    }

    /// <summary>
    /// Renders the button and its centered text label using the provided <see cref="SpriteBatch"/>.
    /// </summary>
    /// <param name="spriteBatch">The target sprite batch.</param>
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

        ColorRgba tint = _state switch
        {
            ControlState.Normal => this.NormalTint,
            ControlState.Hovered => this.HoverTint,
            ControlState.Pressed => this.PressedTint,
            ControlState.Disabled => this.DisabledTint,
            _ => this.NormalTint
        };

        // Render nine-slice skin if available
        if (this.Texture != null && this.SourceBounds.Width > 0f && this.SourceBounds.Height > 0f)
        {
            spriteBatch.DrawNineSlice(
                this.Texture,
                this.SourceBounds,
                this.Margins,
                this.Bounds,
                tint,
                depth,
                footY,
                layer);
        }

        // Render centered text label if available
        if (this.Font != null && !string.IsNullOrEmpty(this.Text))
        {
            TextLayoutOptions options = new(
                maxWidth: this.Bounds.Width,
                wordWrap: false,
                alignment: TextAlignment.Center);

            TextMeasureResult measure = this.Font.MeasureString(this.Text.AsSpan(), options);

            float textY = this.Bounds.Y + ((this.Bounds.Height - measure.Height) * 0.5f);
            if (_state == ControlState.Pressed)
            {
                textY += 1f; // Slight tactile press displacement
            }

            Vector2 textPos = new(this.Bounds.X, textY);
            spriteBatch.DrawString(
                this.Font,
                this.Text.AsSpan(),
                textPos,
                options,
                this.TextColor,
                scale: 1.0f,
                depth: depth,
                footY: footY,
                layer: layer);
        }
    }
}
