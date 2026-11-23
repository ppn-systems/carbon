// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Graphics.Scenes.Objects;

/// <summary>
/// In-world floating text visual object (Nameplates, Floating Combat Text, Guild titles, Chat bubbles) rendered using <see cref="BitmapFont"/>.
/// </summary>
public class TextObject : RenderObject
{
    private BitmapFont _font;
    private string _text = string.Empty;

    /// <summary>
    /// Gets or sets the bitmap font asset used for glyph rasterization.
    /// </summary>
    public BitmapFont Font
    {
        get => _font;
        set => _font = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets or sets the string caption text to render.
    /// </summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    /// <summary>
    /// Gets or sets the depth buffer value for 3D sorting.
    /// </summary>
    public float Depth { get; set; }

    /// <summary>
    /// Gets or sets the explicit feet Y position for 2.5D depth ordering (0 to default to Position.Y).
    /// </summary>
    public float FootY { get; set; }

    /// <inheritdoc/>
    public override RectangleF Bounds
    {
        get
        {
            if (string.IsNullOrEmpty(_text))
            {
                return new RectangleF(this.Position.X, this.Position.Y, 0f, 0f);
            }

            TextMeasureResult measure = _font.MeasureString(_text.AsSpan(), scale: this.Scale.X);

            return new RectangleF(
                this.Position.X - (this.Origin.X * this.Scale.X),
                this.Position.Y - (this.Origin.Y * this.Scale.Y),
                measure.Width,
                measure.Height);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TextObject"/> class.
    /// </summary>
    /// <param name="font">The bitmap font.</param>
    /// <param name="text">Initial text string.</param>
    public TextObject(BitmapFont font, string text = "")
    {
        _font = font ?? throw new ArgumentNullException(nameof(font));
        _text = text ?? string.Empty;
    }

    /// <inheritdoc/>
    public override void Draw(SpriteBatch spriteBatch)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));

        if (!this.IsVisible || string.IsNullOrEmpty(_text))
        {
            return;
        }

        float footY = this.FootY == 0f ? this.Position.Y : this.FootY;

        spriteBatch.DrawString(
            _font,
            _text.AsSpan(),
            this.Position,
            this.Color,
            this.Scale.X,
            this.Depth,
            footY,
            this.Layer);
    }
}
