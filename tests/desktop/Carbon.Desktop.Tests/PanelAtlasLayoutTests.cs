// Copyright (c) 2026 PPN Systems. All rights reserved.

using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Graphics.Batching.Common;
using FluentAssertions;
using Xunit;

namespace Carbon.Desktop.Tests;

/// <summary>Audit round 2, PERF-8: all UI panel images share one atlas without changing a sampled texel.</summary>
public sealed class PanelAtlasLayoutTests
{
    [Fact]
    public void Pack_TwoImages_CopiesEveryBodyAndFramePixelToItsSourceRect()
    {
        PanelImage a = Image(5, 3, seed: 1);
        PanelImage b = Image(4, 6, seed: 2);

        PanelAtlasLayout atlas = PanelAtlasLayout.Pack([a, b]);

        foreach (PanelImage image in new[] { a, b })
        {
            AssertCopied(atlas, image.BodySource, image.Body, image.Width, image.Height);
            AssertCopied(atlas, image.FrameSource, image.Frame, image.Width, image.Height);
        }
    }

    [Fact]
    public void Pack_Gutter_RepeatsEdgePixelsLikeClampToEdge()
    {
        PanelImage a = Image(3, 2, seed: 7);
        PanelImage b = Image(2, 2, seed: 9);

        PanelAtlasLayout atlas = PanelAtlasLayout.Pack([a, b]);

        RectangleF r = a.FrameSource;
        Pixel(atlas, (int)r.X - 1, (int)r.Y).Should().Be(Src(a.Frame, a.Width, 0, 0), "the left gutter repeats column 0");
        Pixel(atlas, (int)r.Right, (int)r.Bottom - 1).Should().Be(Src(a.Frame, a.Width, a.Width - 1, a.Height - 1), "the right gutter repeats the last column");
        Pixel(atlas, (int)r.X, (int)r.Y - 1).Should().Be(Src(a.Frame, a.Width, 0, 0), "the top gutter repeats row 0");
        Pixel(atlas, (int)r.X + 1, (int)r.Bottom).Should().Be(Src(a.Frame, a.Width, 1, a.Height - 1), "the bottom gutter repeats the last row");
    }

    [Fact]
    public void Pack_SourceRects_NeverOverlapIncludingGutters()
    {
        PanelImage a = Image(8, 8, seed: 3);
        PanelImage b = Image(6, 4, seed: 4);
        PanelImage c = Image(10, 2, seed: 5);

        _ = PanelAtlasLayout.Pack([a, b, c]);

        RectangleF[] rects = [a.BodySource, a.FrameSource, b.BodySource, b.FrameSource, c.BodySource, c.FrameSource];
        for (int i = 0; i < rects.Length; i++)
        {
            for (int j = i + 1; j < rects.Length; j++)
            {
                RectangleF gi = new(rects[i].X - 1, rects[i].Y - 1, rects[i].Width + 2, rects[i].Height + 2);
                RectangleF gj = new(rects[j].X - 1, rects[j].Y - 1, rects[j].Width + 2, rects[j].Height + 2);
                gi.Intersects(gj).Should().BeFalse($"cells {i} and {j} must not share texels");
            }
        }
    }

    private static void AssertCopied(PanelAtlasLayout atlas, RectangleF source, byte[] src, int w, int h)
    {
        source.Width.Should().Be(w);
        source.Height.Should().Be(h);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Pixel(atlas, (int)source.X + x, (int)source.Y + y).Should().Be(Src(src, w, x, y));
            }
        }
    }

    private static uint Pixel(PanelAtlasLayout atlas, int x, int y)
    {
        int i = ((y * atlas.Width) + x) * 4;
        return (uint)(atlas.Pixels[i] | (atlas.Pixels[i + 1] << 8) | (atlas.Pixels[i + 2] << 16) | (atlas.Pixels[i + 3] << 24));
    }

    private static uint Src(byte[] src, int w, int x, int y)
    {
        int i = ((y * w) + x) * 4;
        return (uint)(src[i] | (src[i + 1] << 8) | (src[i + 2] << 16) | (src[i + 3] << 24));
    }

    private static PanelImage Image(int w, int h, int seed)
    {
        byte[] body = new byte[w * h * 4];
        byte[] frame = new byte[w * h * 4];
        for (int i = 0; i < body.Length; i++)
        {
            body[i] = (byte)((i * 31) + seed);
            frame[i] = (byte)((i * 17) + (seed * 3) + 1);
        }

        return new PanelImage(w, h, body, frame, HasFile: true);
    }
}
