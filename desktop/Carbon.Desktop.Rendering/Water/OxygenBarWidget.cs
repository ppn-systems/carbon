// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Water;

/// <summary>
/// The oxygen bar (ADR-032 D13): shown while diving and until the breath has refilled, just above the vitals
/// (<see cref="HudLayout.GetOxygenBarRect"/>). It turns red and pulses when the breath is nearly gone, and reads
/// "Out of air!" once the player is drowning. All text comes from the <c>[water]</c> section.
/// </summary>
internal sealed class OxygenBarWidget
{
    /// <summary>The fraction of the breath below which the bar warns.</summary>
    internal const float LowFraction = 0.25f;

    private static readonly ColorRgba s_fill = new(86, 200, 238, 255);
    private static readonly ColorRgba s_lowFill = new(232, 84, 72, 255);

    private readonly CachedText _label = new();

    /// <summary>Draws the bar's frame and fill.</summary>
    /// <param name="batch">The screen-space geometry batch.</param>
    /// <param name="water">The local water view.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="time">Animation time in seconds.</param>
    public static void DrawGeometry(GeometryBatch batch, in WaterView water, int viewportWidth, int viewportHeight, float time)
    {
        ArgumentNullException.ThrowIfNull(batch);
        if (!water.ShowsOxygen)
        {
            return;
        }

        RectangleF rect = HudLayout.GetOxygenBarRect(viewportWidth, viewportHeight);
        float fraction = water.OxygenFraction;
        batch.FillRectangle(new RectangleF(rect.X - 1f, rect.Y - 1f, rect.Width + 2f, rect.Height + 2f), new ColorRgba(8, 10, 14, 245));
        batch.FillRectangle(rect, CarbonUiPalette.SlotFill);

        bool low = fraction < LowFraction;
        ColorRgba fill = low ? s_lowFill : s_fill;
        if (low)
        {
            byte pulse = (byte)(190 + (65 * (0.5f + (0.5f * MathF.Sin(time * 9f)))));
            fill = fill with { A = pulse };
        }

        float width = rect.Width * fraction;
        if (width > 0.5f)
        {
            batch.FillRectangle(new RectangleF(rect.X, rect.Y, width, rect.Height), fill);
            batch.FillRectangle(new RectangleF(rect.X, rect.Y, width, 2f), new ColorRgba(255, 255, 255, 60));
        }

        // A tick every fifth of the breath, so the player can read how long is left at a glance.
        for (int i = 1; i < 5; i++)
        {
            float x = rect.X + (rect.Width * i / 5f);
            batch.DrawLine(new System.Numerics.Vector2(x, rect.Y + 2f), new System.Numerics.Vector2(x, rect.Y + rect.Height - 2f), new ColorRgba(0, 0, 0, 90), 1f);
        }
    }

    /// <summary>Draws the bar's label: seconds of air left, or the drowning warning.</summary>
    /// <param name="spriteBatch">The sprite batch.</param>
    /// <param name="font">The HUD font.</param>
    /// <param name="water">The local water view.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, in WaterView water, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(font);
        if (!water.ShowsOxygen)
        {
            return;
        }

        RectangleF rect = HudLayout.GetOxygenBarRect(viewportWidth, viewportHeight);
        string text = this.Label(in water);
        CarbonUiSkin.DrawCenteredText(
            spriteBatch,
            font,
            text,
            rect,
            water.OxygenSeconds <= 0f ? CarbonUiPalette.TextWarm : CarbonUiPalette.TextPrimary,
            CarbonUiPalette.TextOutline,
            0.85f);
    }

    /// <summary>Gets the label for a water view: "Out of air!" at zero, otherwise the whole seconds left.</summary>
    /// <param name="water">The local water view.</param>
    /// <returns>The localized label.</returns>
    internal string Label(in WaterView water) => water.OxygenSeconds <= 0f
        ? GameText.Get("water.out_of_air")
        : _label.Format("water.oxygen_left", new TextArg("seconds", (long)MathF.Ceiling(water.OxygenSeconds)));
}
