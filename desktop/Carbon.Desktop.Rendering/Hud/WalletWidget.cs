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
/// Compact gold and diamond counter docked to the right of the hotbar. Balances come from the server's
/// <c>WalletSync</c> push (ADR-012); the client never computes them.
/// </summary>
internal sealed class WalletWidget
{
    private const float HotbarWidth = 526f;
    private const float HotbarSlotSize = 54f;
    private const float Width = 132f;
    private const float RowHeight = 26f;
    private const float IconSize = 20f;

    private static readonly ColorRgba s_goldText = new(250, 204, 21, 255);
    private static readonly ColorRgba s_diamondText = new(103, 232, 249, 255);

    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(skin);
        (float x, float y) = Origin(viewportWidth, viewportHeight);
        skin.DrawCard(spriteBatch, new RectangleF(x, y, Width, (RowHeight * 2f) + 10f), CarbonUiPalette.BorderOuter, bodyAlpha: 230);
    }

    public void DrawGeometry(GeometryBatch batch, IVectorArtRepository? art, int viewportWidth, int viewportHeight)
    {
        if (art is null)
        {
            return;
        }

        (float x, float y) = Origin(viewportWidth, viewportHeight);
        Vector2 iconCenter = new(x + 18f, y + 5f + (RowHeight * 0.5f));
        if (art.TryGetUiIcon("coin", out CompiledItemVectorArt? coin))
        {
            VectorRigRenderer.DrawItemIcon(batch, coin, iconCenter, IconSize);
        }

        if (art.TryGetUiIcon("diamond", out CompiledItemVectorArt? diamond))
        {
            VectorRigRenderer.DrawItemIcon(batch, diamond, iconCenter + new Vector2(0f, RowHeight), IconSize);
        }
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, IWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        (float x, float y) = Origin(viewportWidth, viewportHeight);
        ColorRgba outline = CarbonUiPalette.TextOutline;
        spriteBatch.DrawOutlinedString(font, Format(world.LocalPlayerGold), new Vector2(x + 34f, y + 11f), s_goldText, outline, 1, 1f);
        spriteBatch.DrawOutlinedString(font, Format(world.LocalPlayerDiamonds), new Vector2(x + 34f, y + 11f + RowHeight), s_diamondText, outline, 1, 1f);
    }

    private static (float X, float Y) Origin(int viewportWidth, int viewportHeight)
    {
        float hotbarStartX = MathF.Floor((viewportWidth - HotbarWidth) * 0.5f);
        float hotbarY = viewportHeight - HotbarSlotSize - 16f;
        return (hotbarStartX + HotbarWidth + 14f, hotbarY - 7f);
    }

    private static string Format(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
