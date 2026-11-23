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
/// Represents an auto-sizing hover tooltip panel for MMORPG item descriptions, spells, and player stats.
/// Automatically expands its nine-slice background based on measured text dimensions.
/// </summary>
public sealed class Tooltip
{
    /// <summary>
    /// Gets or sets the text description shown in this tooltip.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum line width in pixels before word wrapping.
    /// </summary>
    public float MaxWidth { get; set; } = 280f;

    /// <summary>
    /// Gets or sets the font used for the tooltip text.
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
    /// Gets or sets the nine-slice margins for the tooltip border.
    /// </summary>
    public NineSliceMargins BackgroundMargins { get; set; } = new(6f);

    /// <summary>
    /// Gets or sets the internal text padding inside the panel.
    /// </summary>
    public Padding Padding { get; set; } = new(8f, 6f);

    /// <summary>
    /// Gets or sets the background panel color tint.
    /// </summary>
    public ColorRgba BackgroundColor { get; set; } = new(20, 20, 25, 230);

    /// <summary>
    /// Gets or sets the text color tint.
    /// </summary>
    public ColorRgba TextColor { get; set; } = ColorRgba.White;

    /// <summary>
    /// Initializes a new instance of the <see cref="Tooltip"/> class.
    /// </summary>
    /// <param name="font">Font asset.</param>
    /// <param name="maxWidth">Maximum wrapping width.</param>
    public Tooltip(BitmapFont? font = null, float maxWidth = 280f)
    {
        this.Font = font;
        this.MaxWidth = maxWidth;
    }

    /// <summary>
    /// Renders the tooltip anchored above or beside the cursor, clamped to the viewport bounds.
    /// </summary>
    /// <param name="spriteBatch">Target sprite batch.</param>
    /// <param name="cursorPosition">Mouse cursor position.</param>
    /// <param name="viewport">Total screen viewport rectangle.</param>
    /// <param name="depth">Depth buffer value.</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting.</param>
    /// <param name="layer">Rendering layer index.</param>
    public void Draw(
        SpriteBatch spriteBatch,
        Vector2 cursorPosition,
        in RectangleF viewport,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));

        if (this.Font == null || string.IsNullOrEmpty(this.Text))
        {
            return;
        }

        TextLayoutOptions options = new(
            maxWidth: this.MaxWidth,
            wordWrap: true,
            alignment: TextAlignment.Left);

        TextMeasureResult measure = this.Font.MeasureString(this.Text.AsSpan(), options);

        float panelWidth = measure.Width + this.Padding.Horizontal;
        float panelHeight = measure.Height + this.Padding.Vertical;

        // Position tooltip slightly offset from cursor (bottom-right default)
        float x = cursorPosition.X + 16f;
        float y = cursorPosition.Y + 16f;

        // Clamp to viewport edges so tooltip never clips outside screen
        if (x + panelWidth > viewport.Right)
        {
            x = cursorPosition.X - panelWidth - 4f;
        }

        if (y + panelHeight > viewport.Bottom)
        {
            y = cursorPosition.Y - panelHeight - 4f;
        }

        x = Math.Max(viewport.X, x);
        y = Math.Max(viewport.Y, y);

        RectangleF panelRect = new(x, y, panelWidth, panelHeight);
        float finalFootY = footY == 0f ? panelRect.Bottom : footY;

        // 1. Draw Nine-Slice Background
        if (this.BackgroundTexture != null && this.BackgroundSource.Width > 0f)
        {
            spriteBatch.DrawNineSlice(
                this.BackgroundTexture,
                this.BackgroundSource,
                this.BackgroundMargins,
                panelRect,
                this.BackgroundColor,
                depth,
                finalFootY,
                layer);
        }

        // 2. Draw Text Content
        Vector2 textPos = new(x + this.Padding.Left, y + this.Padding.Top);
        spriteBatch.DrawString(
            this.Font,
            this.Text.AsSpan(),
            textPos,
            options,
            this.TextColor,
            scale: 1.0f,
            depth: depth,
            footY: finalFootY,
            layer: layer);
    }
}
