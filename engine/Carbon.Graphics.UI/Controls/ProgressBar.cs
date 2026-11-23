// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.UI.NineSlice;

namespace Carbon.Graphics.UI.Controls;

/// <summary>
/// Represents a high-performance progress bar for MMORPG Health, Mana, EXP, and Cast bars.
/// Supports smooth ghost bar interpolation (damage delayed trail) and text overlays.
/// </summary>
public sealed class ProgressBar
{
    private float _ghostProgress = 1.0f;

    /// <summary>
    /// Gets or sets the screen bounds of this progress bar.
    /// </summary>
    public RectangleF Bounds { get; set; }

    /// <summary>
    /// Gets or sets the current numeric value (e.g. current HP).
    /// </summary>
    public float Value { get; set; } = 100f;

    /// <summary>
    /// Gets or sets the maximum numeric value (e.g. maximum HP).
    /// </summary>
    public float MaxValue { get; set; } = 100f;

    /// <summary>
    /// Gets the normalized progress fraction [0.0 - 1.0].
    /// </summary>
    public float Progress => this.MaxValue > 0f ? Math.Clamp(this.Value / this.MaxValue, 0f, 1f) : 0f;

    /// <summary>
    /// Gets the normalized ghost progress fraction [0.0 - 1.0] trailing behind damage.
    /// </summary>
    public float GhostProgress => _ghostProgress;

    /// <summary>
    /// Gets or sets the ghost bar catch-up animation speed (units per second).
    /// </summary>
    public float GhostSpeed { get; set; } = 1.5f;

    /// <summary>
    /// Gets or sets the color tint of the primary fill bar.
    /// </summary>
    public ColorRgba FillColor { get; set; } = new(220, 40, 40, 255); // Red default (HP)

    /// <summary>
    /// Gets or sets the color tint of the ghost trail bar.
    /// </summary>
    public ColorRgba GhostColor { get; set; } = new(255, 200, 50, 255); // Yellow default

    /// <summary>
    /// Gets or sets the background track color tint.
    /// </summary>
    public ColorRgba BackgroundColor { get; set; } = new(40, 40, 40, 255);

    /// <summary>
    /// Gets or sets the overlay text color.
    /// </summary>
    public ColorRgba TextColor { get; set; } = ColorRgba.White;

    /// <summary>
    /// Gets or sets whether text numbers are drawn over the bar.
    /// </summary>
    public bool ShowText { get; set; } = true;

    /// <summary>
    /// Gets or sets the font for rendering numbers.
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
    /// Gets or sets the fill skin texture.
    /// </summary>
    public ITexture? FillTexture { get; set; }

    /// <summary>
    /// Gets or sets the fill texture source bounds.
    /// </summary>
    public RectangleF FillSource { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ProgressBar"/> class.
    /// </summary>
    /// <param name="bounds">Screen bounds.</param>
    /// <param name="value">Initial value.</param>
    /// <param name="maxValue">Maximum value.</param>
    public ProgressBar(in RectangleF bounds, float value = 100f, float maxValue = 100f)
    {
        this.Bounds = bounds;
        this.Value = value;
        this.MaxValue = maxValue;
        _ghostProgress = this.Progress;
    }

    /// <summary>
    /// Updates the ghost damage trail interpolation by elapsed delta time.
    /// </summary>
    /// <param name="deltaTime">Elapsed time in seconds.</param>
    public void Update(float deltaTime)
    {
        float target = this.Progress;
        if (_ghostProgress > target)
        {
            _ghostProgress = Math.Max(target, _ghostProgress - (this.GhostSpeed * deltaTime));
        }
        else
        {
            _ghostProgress = target;
        }
    }

    /// <summary>
    /// Renders the progress bar into the sprite batch.
    /// Zero heap allocation.
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

        // 1. Draw Background Track
        if (this.BackgroundTexture != null && this.BackgroundSource.Width > 0f)
        {
            spriteBatch.DrawNineSlice(
                this.BackgroundTexture,
                this.BackgroundSource,
                this.BackgroundMargins,
                this.Bounds,
                this.BackgroundColor,
                depth,
                finalFootY,
                layer);
        }

        // 2. Draw Ghost Bar (Delayed damage trail)
        if (_ghostProgress > this.Progress)
        {
            float ghostWidth = this.Bounds.Width * _ghostProgress;
            RectangleF ghostRect = new(this.Bounds.X, this.Bounds.Y, ghostWidth, this.Bounds.Height);

            if (this.FillTexture != null)
            {
                spriteBatch.Draw(
                    this.FillTexture,
                    new Vector2(ghostRect.X, ghostRect.Y),
                    this.FillSource.Width > 0f ? this.FillSource : null,
                    this.GhostColor,
                    rotation: 0f,
                    origin: Vector2.Zero,
                    scale: new Vector2(ghostRect.Width / (this.FillSource.Width > 0f ? this.FillSource.Width : 1f), ghostRect.Height / (this.FillSource.Height > 0f ? this.FillSource.Height : 1f)),
                    effects: SpriteEffects.None,
                    depth: depth,
                    footY: finalFootY,
                    layer: layer);
            }
        }

        // 3. Draw Active Fill Bar
        float fillWidth = this.Bounds.Width * this.Progress;
        if (fillWidth > 0f)
        {
            RectangleF fillRect = new(this.Bounds.X, this.Bounds.Y, fillWidth, this.Bounds.Height);

            if (this.FillTexture != null)
            {
                spriteBatch.Draw(
                    this.FillTexture,
                    new Vector2(fillRect.X, fillRect.Y),
                    this.FillSource.Width > 0f ? this.FillSource : null,
                    this.FillColor,
                    rotation: 0f,
                    origin: Vector2.Zero,
                    scale: new Vector2(fillRect.Width / (this.FillSource.Width > 0f ? this.FillSource.Width : 1f), fillRect.Height / (this.FillSource.Height > 0f ? this.FillSource.Height : 1f)),
                    effects: SpriteEffects.None,
                    depth: depth,
                    footY: finalFootY,
                    layer: layer);
            }
        }

        // 4. Draw Overlay Text (e.g. "1,500 / 2,000")
        if (this.ShowText && this.Font != null)
        {
            Span<char> buffer = stackalloc char[64];
            int written = 0;

            if (this.Value.TryFormat(buffer[written..], out int valWritten, "N0", CultureInfo.InvariantCulture))
            {
                written += valWritten;
                if (" / ".AsSpan().TryCopyTo(buffer[written..]))
                {
                    written += 3;
                    if (this.MaxValue.TryFormat(buffer[written..], out int maxWritten, "N0", CultureInfo.InvariantCulture))
                    {
                        written += maxWritten;
                    }
                }
            }

            ReadOnlySpan<char> textSpan = buffer[..written];
            TextLayoutOptions options = new(
                maxWidth: this.Bounds.Width,
                wordWrap: false,
                alignment: TextAlignment.Center);

            TextMeasureResult measure = this.Font.MeasureString(textSpan, options);
            float textY = this.Bounds.Y + ((this.Bounds.Height - measure.Height) * 0.5f);

            spriteBatch.DrawOutlinedString(
                this.Font,
                textSpan,
                new Vector2(this.Bounds.X, textY),
                options,
                this.TextColor,
                ColorRgba.Black,
                thickness: 1,
                scale: 1.0f,
                depth: depth,
                footY: finalFootY,
                layer: layer);
        }
    }
}
