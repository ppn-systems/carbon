// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.OpenGL.Abstractions;

namespace Carbon.Graphics.UI.NineSlice;

/// <summary>
/// Provides extension methods for rendering nine-slice panels and windows via <see cref="SpriteBatch"/>.
/// Zero heap allocation on draw hot paths.
/// </summary>
public static class NineSliceRenderer
{
    /// <summary>
    /// Renders a nine-slice scalable panel into the sprite batch.
    /// </summary>
    /// <param name="spriteBatch">The target sprite batch.</param>
    /// <param name="texture">The panel skin texture.</param>
    /// <param name="sourceBounds">The pixel rectangle on the source texture.</param>
    /// <param name="margins">The border margins.</param>
    /// <param name="destination">The destination rectangle on screen.</param>
    /// <param name="color">The color tint applied to the panel.</param>
    /// <param name="depth">Depth buffer value.</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting.</param>
    /// <param name="layer">Rendering layer index.</param>
    public static void DrawNineSlice(
        this SpriteBatch spriteBatch,
        ITexture texture,
        in RectangleF sourceBounds,
        in NineSliceMargins margins,
        in RectangleF destination,
        ColorRgba color,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch, nameof(spriteBatch));
        ArgumentNullException.ThrowIfNull(texture, nameof(texture));

        if (destination.Width <= 0f || destination.Height <= 0f)
        {
            return;
        }

        NineSliceGrid grid = NineSliceGrid.Create(sourceBounds, margins, destination);
        float finalFootY = footY == 0f ? destination.Bottom : footY;

        // Render each valid slice
        DrawSlice(spriteBatch, texture, grid.SourceTopLeft, grid.DestTopLeft, color, depth, finalFootY, layer);
        DrawSlice(spriteBatch, texture, grid.SourceTopCenter, grid.DestTopCenter, color, depth, finalFootY, layer);
        DrawSlice(spriteBatch, texture, grid.SourceTopRight, grid.DestTopRight, color, depth, finalFootY, layer);

        DrawSlice(spriteBatch, texture, grid.SourceCenterLeft, grid.DestCenterLeft, color, depth, finalFootY, layer);
        DrawSlice(spriteBatch, texture, grid.SourceCenter, grid.DestCenter, color, depth, finalFootY, layer);
        DrawSlice(spriteBatch, texture, grid.SourceCenterRight, grid.DestCenterRight, color, depth, finalFootY, layer);

        DrawSlice(spriteBatch, texture, grid.SourceBottomLeft, grid.DestBottomLeft, color, depth, finalFootY, layer);
        DrawSlice(spriteBatch, texture, grid.SourceBottomCenter, grid.DestBottomCenter, color, depth, finalFootY, layer);
        DrawSlice(spriteBatch, texture, grid.SourceBottomRight, grid.DestBottomRight, color, depth, finalFootY, layer);
    }

    private static void DrawSlice(
        SpriteBatch spriteBatch,
        ITexture texture,
        in RectangleF source,
        in RectangleF dest,
        ColorRgba color,
        float depth,
        float footY,
        int layer)
    {
        if (dest.Width <= 0f || dest.Height <= 0f || source.Width <= 0f || source.Height <= 0f)
        {
            return;
        }

        Vector2 scale = new(dest.Width / source.Width, dest.Height / source.Height);
        Vector2 position = new(dest.X, dest.Y);

        spriteBatch.Draw(
            texture,
            position,
            source,
            color,
            rotation: 0f,
            origin: Vector2.Zero,
            scale: scale,
            effects: SpriteEffects.None,
            depth: depth,
            footY: footY,
            layer: layer);
    }
}
