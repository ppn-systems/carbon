// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Draws the enhancement treatment of a slot (fill, glow, shimmer, corner <c>+N</c> badge) the same way in every widget.
/// The slot's own border stays the rarity colour.
/// </summary>
internal static class EnhancementSlotDecor
{
    /// <summary>Fills the slot background with the tier colour (geometry pass, under the item icon).</summary>
    public static void DrawFill(GeometryBatch batch, in RectangleF rect, byte level, float inset)
    {
        if (level == 0)
        {
            return;
        }

        ref readonly EnhancementTierStyle style = ref EnhancementTierStyles.Get(level);
        batch.FillRectangle(new RectangleF(rect.X + inset, rect.Y + inset, rect.Width - (inset * 2f), rect.Height - (inset * 2f)), style.Fill);
    }

    /// <summary>
    /// Draws the glow rings, the shimmer sweep and the badge plate (geometry pass, over the slot frame).
    /// <paramref name="scale"/> is the UI scale and <paramref name="animTime"/> the HUD clock in seconds.
    /// </summary>
    public static void DrawOverlay(GeometryBatch batch, in RectangleF rect, byte level, float scale, float animTime)
    {
        if (level == 0)
        {
            return;
        }

        ref readonly EnhancementTierStyle style = ref EnhancementTierStyles.Get(level);
        if (style.HasGlow)
        {
            float pulse = 0.8f + (0.2f * MathF.Sin(animTime * 2.4f));
            for (int ring = 1; ring <= 3; ring++)
            {
                float grow = ring * MathF.Max(1.5f, 1.6f * scale);
                RectangleF r = new(rect.X - grow, rect.Y - grow, rect.Width + (grow * 2f), rect.Height + (grow * 2f));
                batch.DrawRectangle(r, EnhancementTierStyles.Fade(style.Glow, pulse / ring), MathF.Max(1.5f, 1.6f * scale));
            }
        }

        if (style.Shimmer)
        {
            // A soft highlight sweeps left to right every 1.6 s, clipped to the slot.
            float phase = animTime / 1.6f % 1f;
            float bandW = rect.Width * 0.22f;
            float x = rect.X - bandW + (phase * (rect.Width + bandW));
            float left = MathF.Max(rect.X + 2f, x);
            float right = MathF.Min(rect.Right - 2f, x + bandW);
            if (right > left)
            {
                batch.FillRectangle(new RectangleF(left, rect.Y + 2f, right - left, rect.Height - 4f), EnhancementTierStyles.Fade(style.Accent, 0.16f));
                float coreL = MathF.Max(left, x + (bandW * 0.4f));
                float coreR = MathF.Min(right, x + (bandW * 0.6f));
                if (coreR > coreL)
                {
                    batch.FillRectangle(new RectangleF(coreL, rect.Y + 2f, coreR - coreL, rect.Height - 4f), EnhancementTierStyles.Fade(style.Accent, 0.22f));
                }
            }
        }

        RectangleF plate = GetBadgeRect(in rect, level, scale);
        batch.FillRectangle(plate, new ColorRgba(8, 10, 14, 230));
        batch.DrawRectangle(plate, style.Accent, MathF.Max(1f, 1.2f * scale));
    }

    /// <summary>Draws the <c>+N</c> badge text on its plate (text pass).</summary>
    public static void DrawBadgeText(SpriteBatch spriteBatch, BitmapFont font, in RectangleF rect, byte level, float textScale, float scale)
    {
        if (level == 0)
        {
            return;
        }

        string badge = EnhancementTierStyles.Badge(level);
        RectangleF plate = GetBadgeRect(in rect, level, scale);
        TextMeasureResult size = font.MeasureString(badge, TextLayoutOptions.Default, textScale);
        spriteBatch.DrawOutlinedString(
            font,
            badge,
            new System.Numerics.Vector2(plate.X + MathF.Floor((plate.Width - size.Width) * 0.5f), plate.Y + MathF.Floor((plate.Height - size.Height) * 0.5f)),
            EnhancementTierStyles.Get(level).Accent,
            CarbonUiPalette.TextOutline,
            1,
            textScale);
    }

    /// <summary>Gets the corner plate behind the badge (top-right of the slot).</summary>
    public static RectangleF GetBadgeRect(in RectangleF rect, byte level, float scale)
    {
        float h = MathF.Round(15f * scale);
        float w = MathF.Round((level >= 10 ? 27f : 21f) * scale);
        float pad = MathF.Round(3f * scale);
        return new RectangleF(rect.Right - w - pad, rect.Y + pad, w, h);
    }
}
