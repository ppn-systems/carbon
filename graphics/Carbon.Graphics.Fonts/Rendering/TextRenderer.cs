// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;

namespace Carbon.Graphics.Fonts.Rendering;

/// <summary>
/// Provides extension methods on <see cref="SpriteBatch"/> for high-throughput, zero-allocation text rendering.
/// Includes support for solid outlines and drop shadows for 2.5D MMORPG nameplates and floating combat numbers.
/// </summary>
public static class TextRenderer
{
    /// <summary>
    /// Renders a span of characters at the specified screen or world position.
    /// Zero heap allocation.
    /// </summary>
    /// <param name="spriteBatch">The target sprite batch.</param>
    /// <param name="font">The bitmap font asset.</param>
    /// <param name="text">The character span to render.</param>
    /// <param name="position">Top-left placement coordinates.</param>
    /// <param name="color">Text tint color.</param>
    /// <param name="scale">Scale multiplier (default 1.0).</param>
    /// <param name="depth">Render depth buffer value.</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting.</param>
    /// <param name="layer">Rendering layer index.</param>
    public static void DrawString(
        this SpriteBatch spriteBatch,
        BitmapFont font,
        ReadOnlySpan<char> text,
        Vector2 position,
        ColorRgba color,
        float scale = 1.0f,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(font, nameof(font));

        TextLayoutEngine.LayoutAndDraw(
            spriteBatch,
            font.Atlas,
            font.Metrics,
            text,
            TextLayoutOptions.Default,
            position,
            color,
            scale,
            depth,
            footY,
            layer);
    }

    /// <summary>
    /// Renders text with customized layout constraints such as word wrapping and alignment.
    /// </summary>
    /// <param name="spriteBatch">The target sprite batch.</param>
    /// <param name="font">The bitmap font asset.</param>
    /// <param name="text">The character span to render.</param>
    /// <param name="position">Top-left placement coordinates.</param>
    /// <param name="options">Layout constraints and alignment.</param>
    /// <param name="color">Text tint color.</param>
    /// <param name="scale">Scale multiplier.</param>
    /// <param name="depth">Render depth buffer value.</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting.</param>
    /// <param name="layer">Rendering layer index.</param>
    public static void DrawString(
        this SpriteBatch spriteBatch,
        BitmapFont font,
        ReadOnlySpan<char> text,
        Vector2 position,
        in TextLayoutOptions options,
        ColorRgba color,
        float scale = 1.0f,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(font, nameof(font));

        TextLayoutEngine.LayoutAndDraw(
            spriteBatch,
            font.Atlas,
            font.Metrics,
            text,
            options,
            position,
            color,
            scale,
            depth,
            footY,
            layer);
    }

    /// <summary>
    /// Renders text with a solid high-contrast outline around each glyph.
    /// Essential for MMORPG combat damage numbers and player nameplates so text remains readable against any terrain.
    /// </summary>
    /// <param name="spriteBatch">The target sprite batch.</param>
    /// <param name="font">The bitmap font asset.</param>
    /// <param name="text">The character span to render.</param>
    /// <param name="position">Top-left placement coordinates.</param>
    /// <param name="textColor">Primary fill color.</param>
    /// <param name="outlineColor">Solid outline border color.</param>
    /// <param name="thickness">Outline pixel thickness (default 1).</param>
    /// <param name="scale">Scale multiplier.</param>
    /// <param name="depth">Render depth buffer value.</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting.</param>
    /// <param name="layer">Rendering layer index.</param>
    public static void DrawOutlinedString(
        this SpriteBatch spriteBatch,
        BitmapFont font,
        ReadOnlySpan<char> text,
        Vector2 position,
        ColorRgba textColor,
        ColorRgba outlineColor,
        int thickness = 1,
        float scale = 1.0f,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(font, nameof(font));

        int t = Math.Max(1, thickness);
        float offset = t * scale;

        // Render 8 surrounding directions for solid circular outline
        ReadOnlySpan<Vector2> offsets =
        [
            new(-offset, -offset),
            new(0f, -offset),
            new(offset, -offset),
            new(-offset, 0f),
            new(offset, 0f),
            new(-offset, offset),
            new(0f, offset),
            new(offset, offset)
        ];

        for (int i = 0; i < offsets.Length; i++)
        {
            TextLayoutEngine.LayoutAndDraw(
                spriteBatch,
                font.Atlas,
                font.Metrics,
                text,
                TextLayoutOptions.Default,
                position + offsets[i],
                outlineColor,
                scale,
                depth,
                footY,
                layer);
        }

        // Render foreground text on top
        TextLayoutEngine.LayoutAndDraw(
            spriteBatch,
            font.Atlas,
            font.Metrics,
            text,
            TextLayoutOptions.Default,
            position,
            textColor,
            scale,
            depth,
            footY,
            layer);
    }

    /// <summary>
    /// Renders text with customized layout options and a solid outline around each glyph.
    /// </summary>
    /// <param name="spriteBatch">The target sprite batch.</param>
    /// <param name="font">The bitmap font asset.</param>
    /// <param name="text">The character span to render.</param>
    /// <param name="position">Top-left placement coordinates.</param>
    /// <param name="options">Layout constraints and alignment.</param>
    /// <param name="textColor">Primary fill color.</param>
    /// <param name="outlineColor">Solid outline border color.</param>
    /// <param name="thickness">Outline pixel thickness (default 1).</param>
    /// <param name="scale">Scale multiplier.</param>
    /// <param name="depth">Render depth buffer value.</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting.</param>
    /// <param name="layer">Rendering layer index.</param>
    public static void DrawOutlinedString(
        this SpriteBatch spriteBatch,
        BitmapFont font,
        ReadOnlySpan<char> text,
        Vector2 position,
        in TextLayoutOptions options,
        ColorRgba textColor,
        ColorRgba outlineColor,
        int thickness = 1,
        float scale = 1.0f,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(font, nameof(font));

        int t = Math.Max(1, thickness);
        float offset = t * scale;

        ReadOnlySpan<Vector2> offsets =
        [
            new(-offset, -offset),
            new(0f, -offset),
            new(offset, -offset),
            new(-offset, 0f),
            new(offset, 0f),
            new(-offset, offset),
            new(0f, offset),
            new(offset, offset)
        ];

        for (int i = 0; i < offsets.Length; i++)
        {
            TextLayoutEngine.LayoutAndDraw(
                spriteBatch,
                font.Atlas,
                font.Metrics,
                text,
                options,
                position + offsets[i],
                outlineColor,
                scale,
                depth,
                footY,
                layer);
        }

        TextLayoutEngine.LayoutAndDraw(
            spriteBatch,
            font.Atlas,
            font.Metrics,
            text,
            options,
            position,
            textColor,
            scale,
            depth,
            footY,
            layer);
    }

    /// <summary>
    /// Renders text with a directional drop shadow underneath.
    /// </summary>
    /// <param name="spriteBatch">The target sprite batch.</param>
    /// <param name="font">The bitmap font asset.</param>
    /// <param name="text">The character span to render.</param>
    /// <param name="position">Top-left placement coordinates.</param>
    /// <param name="textColor">Primary fill color.</param>
    /// <param name="shadowColor">Shadow color.</param>
    /// <param name="shadowOffset">Shadow offset vector in pixels.</param>
    /// <param name="scale">Scale multiplier.</param>
    /// <param name="depth">Render depth buffer value.</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting.</param>
    /// <param name="layer">Rendering layer index.</param>
    public static void DrawShadowedString(
        this SpriteBatch spriteBatch,
        BitmapFont font,
        ReadOnlySpan<char> text,
        Vector2 position,
        ColorRgba textColor,
        ColorRgba shadowColor,
        Vector2 shadowOffset,
        float scale = 1.0f,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(font, nameof(font));

        // Render shadow first
        TextLayoutEngine.LayoutAndDraw(
            spriteBatch,
            font.Atlas,
            font.Metrics,
            text,
            TextLayoutOptions.Default,
            position + (shadowOffset * scale),
            shadowColor,
            scale,
            depth,
            footY,
            layer);

        // Render foreground text
        TextLayoutEngine.LayoutAndDraw(
            spriteBatch,
            font.Atlas,
            font.Metrics,
            text,
            TextLayoutOptions.Default,
            position,
            textColor,
            scale,
            depth,
            footY,
            layer);
    }

    /// <summary>
    /// Renders text with customized layout constraints and a directional drop shadow underneath.
    /// </summary>
    /// <param name="spriteBatch">The target sprite batch.</param>
    /// <param name="font">The bitmap font asset.</param>
    /// <param name="text">The character span to render.</param>
    /// <param name="position">Top-left placement coordinates.</param>
    /// <param name="options">Layout constraints and alignment.</param>
    /// <param name="textColor">Primary fill color.</param>
    /// <param name="shadowColor">Shadow color.</param>
    /// <param name="shadowOffset">Shadow offset vector in pixels.</param>
    /// <param name="scale">Scale multiplier.</param>
    /// <param name="depth">Render depth buffer value.</param>
    /// <param name="footY">2.5D base feet Y coordinate for depth sorting.</param>
    /// <param name="layer">Rendering layer index.</param>
    public static void DrawShadowedString(
        this SpriteBatch spriteBatch,
        BitmapFont font,
        ReadOnlySpan<char> text,
        Vector2 position,
        in TextLayoutOptions options,
        ColorRgba textColor,
        ColorRgba shadowColor,
        Vector2 shadowOffset,
        float scale = 1.0f,
        float depth = 0f,
        float footY = 0f,
        int layer = 0)
    {
        ArgumentNullException.ThrowIfNull(font, nameof(font));

        // Render shadow first
        TextLayoutEngine.LayoutAndDraw(
            spriteBatch,
            font.Atlas,
            font.Metrics,
            text,
            options,
            position + (shadowOffset * scale),
            shadowColor,
            scale,
            depth,
            footY,
            layer);

        // Render foreground text
        TextLayoutEngine.LayoutAndDraw(
            spriteBatch,
            font.Atlas,
            font.Metrics,
            text,
            options,
            position,
            textColor,
            scale,
            depth,
            footY,
            layer);
    }
}
