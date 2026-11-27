// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Numerics;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Gold and diamond counters in the inventory window's header, between the title and the close button. They are not on
/// the main HUD (owner request: currency only inside the inventory; the trader window shows its own gold line).
/// Balances come from the server's <c>WalletSync</c> push (ADR-012); the client never computes them.
/// </summary>
internal static class WalletWidget
{
    private const float BaseColumnWidth = 118f;
    private const float BaseIconSize = 20f;
    private const float BaseHeight = 30f;
    private const float BaseCloseGap = 16f;

    private static readonly ColorRgba s_goldText = new(250, 204, 21, 255);
    private static readonly ColorRgba s_diamondText = new(103, 232, 249, 255);

    /// <summary>Gets the header strip that holds both counters, right-aligned to the close button.</summary>
    internal static RectangleF GetStripRect(in PlayerModalLayout layout)
    {
        float s = layout.Scale;
        RectangleF close = PlayerModalTabs.GetCloseButtonRect(in layout);
        float width = MathF.Round(BaseColumnWidth * s) * 2f;
        float height = MathF.Round(BaseHeight * s);
        return new RectangleF(close.X - MathF.Round(BaseCloseGap * s) - width, close.Y + MathF.Floor((close.Height - height) * 0.5f), width, height);
    }

    /// <summary>Gets the centre of the icon of column <paramref name="column"/> (<c>0</c>: gold, <c>1</c>: diamonds).</summary>
    internal static Vector2 GetIconCenter(in PlayerModalLayout layout, int column)
    {
        RectangleF strip = GetStripRect(in layout);
        float columnW = strip.Width * 0.5f;
        float icon = MathF.Round(BaseIconSize * layout.Scale);
        return new Vector2(strip.X + (column * columnW) + (icon * 0.5f) + 4f, strip.Y + (strip.Height * 0.5f));
    }

    /// <summary>Gets the top-left of the balance text of column <paramref name="column"/>.</summary>
    internal static Vector2 GetTextOrigin(in PlayerModalLayout layout, int column)
    {
        Vector2 icon = GetIconCenter(in layout, column);
        float iconSize = MathF.Round(BaseIconSize * layout.Scale);
        return new Vector2(icon.X + (iconSize * 0.5f) + MathF.Round(6f * layout.Scale), icon.Y - MathF.Round(8f * layout.TextScale));
    }

    public static void DrawGeometry(GeometryBatch batch, IVectorArtRepository? art, in PlayerModalLayout layout)
    {
        if (art is null)
        {
            return;
        }

        float iconSize = MathF.Round(BaseIconSize * layout.Scale);
        if (art.TryGetUiIcon("coin", out CompiledItemVectorArt? coin))
        {
            VectorRigRenderer.DrawItemIcon(batch, coin, GetIconCenter(in layout, 0), iconSize);
        }

        if (art.TryGetUiIcon("diamond", out CompiledItemVectorArt? diamond))
        {
            VectorRigRenderer.DrawItemIcon(batch, diamond, GetIconCenter(in layout, 1), iconSize);
        }
    }

    public static void DrawText(SpriteBatch spriteBatch, BitmapFont font, IWorldReadModel world, in PlayerModalLayout layout)
    {
        ArgumentNullException.ThrowIfNull(world);
        ColorRgba outline = CarbonUiPalette.TextOutline;
        // Balances are formatted into stack buffers: no string per frame (audit round 2, PERF-7).
        Span<char> gold = stackalloc char[32];
        Span<char> diamonds = stackalloc char[32];
        spriteBatch.DrawOutlinedString(font, Format(world.LocalPlayerGold, gold), GetTextOrigin(in layout, 0), s_goldText, outline, 1, layout.TextScale);
        spriteBatch.DrawOutlinedString(font, Format(world.LocalPlayerDiamonds, diamonds), GetTextOrigin(in layout, 1), s_diamondText, outline, 1, layout.TextScale);
    }

    /// <summary>Formats <paramref name="value"/> as <c>N0</c> (invariant) into <paramref name="buffer"/>; 32 chars fit any long.</summary>
    internal static ReadOnlySpan<char> Format(long value, Span<char> buffer) =>
        value.TryFormat(buffer, out int written, "N0", CultureInfo.InvariantCulture) ? buffer[..written] : [];
}
