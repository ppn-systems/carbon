// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Desktop.Rendering.UiSkin;

/// <summary>A decoded UI panel split into its body mask and frame art (RGBA8, <paramref name="Width"/> × <paramref name="Height"/>).</summary>
internal sealed record PanelImage(int Width, int Height, byte[] Body, byte[] Frame, bool HasFile)
{
    /// <summary>Gets or sets the body's pixel rectangle inside the atlas (set by <see cref="PanelAtlasLayout.Pack"/>).</summary>
    public RectangleF BodySource { get; set; }

    /// <summary>Gets or sets the frame's pixel rectangle inside the atlas (set by <see cref="PanelAtlasLayout.Pack"/>).</summary>
    public RectangleF FrameSource { get; set; }
}

/// <summary>
/// Packs panel bodies and frames side by side into one RGBA8 atlas (audit round 2, PERF-8). Every image gets a
/// one-pixel border that repeats its edge pixels, so nearest-filtered sampling at a slice edge reads the same texel the
/// old one-texture-per-image clamp-to-edge setup did, never a neighbour's.
/// </summary>
internal sealed class PanelAtlasLayout
{
    private const int Gutter = 1;

    private PanelAtlasLayout(int width, int height, byte[] pixels)
    {
        this.Width = width;
        this.Height = height;
        this.Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    /// <summary>Lays <paramref name="images"/> out in one row (body, then frame) and assigns their source rectangles.</summary>
    public static PanelAtlasLayout Pack(IReadOnlyList<PanelImage> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        int width = 0;
        int height = 0;
        for (int i = 0; i < images.Count; i++)
        {
            width += 2 * (images[i].Width + (2 * Gutter));
            height = Math.Max(height, images[i].Height + (2 * Gutter));
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        byte[] pixels = new byte[width * height * 4];
        int x = 0;
        for (int i = 0; i < images.Count; i++)
        {
            PanelImage image = images[i];
            image.BodySource = Blit(pixels, width, x, image.Body, image.Width, image.Height);
            x += image.Width + (2 * Gutter);
            image.FrameSource = Blit(pixels, width, x, image.Frame, image.Width, image.Height);
            x += image.Width + (2 * Gutter);
        }

        return new PanelAtlasLayout(width, height, pixels);
    }

    // Copies a w×h image to (cellX + 1, 1), extruding its edge pixels into the one-pixel gutter around it.
    private static RectangleF Blit(byte[] atlas, int atlasWidth, int cellX, byte[] src, int w, int h)
    {
        for (int y = -Gutter; y < h + Gutter; y++)
        {
            int sy = Math.Clamp(y, 0, h - 1);
            for (int px = -Gutter; px < w + Gutter; px++)
            {
                int sx = Math.Clamp(px, 0, w - 1);
                int srcIdx = ((sy * w) + sx) * 4;
                int dstIdx = (((y + Gutter) * atlasWidth) + cellX + px + Gutter) * 4;
                atlas[dstIdx] = src[srcIdx];
                atlas[dstIdx + 1] = src[srcIdx + 1];
                atlas[dstIdx + 2] = src[srcIdx + 2];
                atlas[dstIdx + 3] = src[srcIdx + 3];
            }
        }

        return new RectangleF(cellX + Gutter, Gutter, w, h);
    }
}
