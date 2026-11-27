// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using Carbon.Graphics.Batching.Common;

namespace Carbon.Graphics.UI.NineSlice;

/// <summary>
/// Calculates and holds the 9 source and destination rectangular regions for nine-slice panel rendering.
/// Zero heap allocation value struct.
/// </summary>
public readonly struct NineSliceGrid
{
    /// <summary>
    /// Gets the top-left corner rectangle on the source texture.
    /// </summary>
    public RectangleF SourceTopLeft { get; }

    /// <summary>
    /// Gets the top-center edge rectangle on the source texture.
    /// </summary>
    public RectangleF SourceTopCenter { get; }

    /// <summary>
    /// Gets the top-right corner rectangle on the source texture.
    /// </summary>
    public RectangleF SourceTopRight { get; }

    /// <summary>
    /// Gets the middle-left edge rectangle on the source texture.
    /// </summary>
    public RectangleF SourceCenterLeft { get; }

    /// <summary>
    /// Gets the central interior rectangle on the source texture.
    /// </summary>
    public RectangleF SourceCenter { get; }

    /// <summary>
    /// Gets the middle-right edge rectangle on the source texture.
    /// </summary>
    public RectangleF SourceCenterRight { get; }

    /// <summary>
    /// Gets the bottom-left corner rectangle on the source texture.
    /// </summary>
    public RectangleF SourceBottomLeft { get; }

    /// <summary>
    /// Gets the bottom-center edge rectangle on the source texture.
    /// </summary>
    public RectangleF SourceBottomCenter { get; }

    /// <summary>
    /// Gets the bottom-right corner rectangle on the source texture.
    /// </summary>
    public RectangleF SourceBottomRight { get; }

    /// <summary>
    /// Gets the top-left corner destination rectangle on screen.
    /// </summary>
    public RectangleF DestTopLeft { get; }

    /// <summary>
    /// Gets the top-center edge destination rectangle on screen.
    /// </summary>
    public RectangleF DestTopCenter { get; }

    /// <summary>
    /// Gets the top-right corner destination rectangle on screen.
    /// </summary>
    public RectangleF DestTopRight { get; }

    /// <summary>
    /// Gets the middle-left edge destination rectangle on screen.
    /// </summary>
    public RectangleF DestCenterLeft { get; }

    /// <summary>
    /// Gets the central interior destination rectangle on screen.
    /// </summary>
    public RectangleF DestCenter { get; }

    /// <summary>
    /// Gets the middle-right edge destination rectangle on screen.
    /// </summary>
    public RectangleF DestCenterRight { get; }

    /// <summary>
    /// Gets the bottom-left corner destination rectangle on screen.
    /// </summary>
    public RectangleF DestBottomLeft { get; }

    /// <summary>
    /// Gets the bottom-center edge destination rectangle on screen.
    /// </summary>
    public RectangleF DestBottomCenter { get; }

    /// <summary>
    /// Gets the bottom-right corner destination rectangle on screen.
    /// </summary>
    public RectangleF DestBottomRight { get; }

    /// <summary>
    /// Computes the 9-slice grid slices from source texture bounds, border margins, and target destination rectangle.
    /// </summary>
    /// <param name="source">The bounding box on the source texture.</param>
    /// <param name="margins">The border margins.</param>
    /// <param name="destination">The destination rectangle on screen.</param>
    /// <returns>A populated <see cref="NineSliceGrid"/> instance.</returns>
    public static NineSliceGrid Create(
        in RectangleF source,
        in NineSliceMargins margins,
        in RectangleF destination) =>
        Create(source, margins, destination, cornerScale: 1f);

    /// <summary>
    /// Computes the 9-slice grid with the destination corners and border strips <paramref name="cornerScale"/> units per
    /// source texel.
    /// </summary>
    /// <param name="source">The bounding box on the source texture.</param>
    /// <param name="margins">The border margins, in source texels.</param>
    /// <param name="destination">The destination rectangle on screen.</param>
    /// <param name="cornerScale">Destination units per source texel for the corners and border strips.</param>
    /// <returns>A populated <see cref="NineSliceGrid"/> instance.</returns>
    public static NineSliceGrid Create(
        in RectangleF source,
        in NineSliceMargins margins,
        in RectangleF destination,
        float cornerScale)
    {
        // Source column widths & row heights
        float srcCol0W = margins.Left;
        float srcCol1W = Math.Max(0f, source.Width - margins.Horizontal);
        float srcCol2W = margins.Right;

        float srcRow0H = margins.Top;
        float srcRow1H = Math.Max(0f, source.Height - margins.Vertical);
        float srcRow2H = margins.Bottom;

        // Source positions
        float srcX0 = source.X;
        float srcX1 = srcX0 + srcCol0W;
        float srcX2 = srcX1 + srcCol1W;

        float srcY0 = source.Y;
        float srcY1 = srcY0 + srcRow0H;
        float srcY2 = srcY1 + srcRow1H;

        // Destination scaling if destination is smaller than margins
        float destHorizontal = margins.Horizontal * cornerScale;
        float destVertical = margins.Vertical * cornerScale;
        float scaleX = destination.Width < destHorizontal && destHorizontal > 0f
            ? destination.Width / margins.Horizontal
            : cornerScale;

        float scaleY = destination.Height < destVertical && destVertical > 0f
            ? destination.Height / margins.Vertical
            : cornerScale;

        float dstCol0W = margins.Left * scaleX;
        float dstCol2W = margins.Right * scaleX;
        float dstCol1W = Math.Max(0f, destination.Width - dstCol0W - dstCol2W);

        float dstRow0H = margins.Top * scaleY;
        float dstRow2H = margins.Bottom * scaleY;
        float dstRow1H = Math.Max(0f, destination.Height - dstRow0H - dstRow2H);

        // Destination positions
        float dstX0 = destination.X;
        float dstX1 = dstX0 + dstCol0W;
        float dstX2 = dstX1 + dstCol1W;

        float dstY0 = destination.Y;
        float dstY1 = dstY0 + dstRow0H;
        float dstY2 = dstY1 + dstRow1H;

        return new NineSliceGrid(
            new RectangleF(srcX0, srcY0, srcCol0W, srcRow0H),
            new RectangleF(srcX1, srcY0, srcCol1W, srcRow0H),
            new RectangleF(srcX2, srcY0, srcCol2W, srcRow0H),
            new RectangleF(srcX0, srcY1, srcCol0W, srcRow1H),
            new RectangleF(srcX1, srcY1, srcCol1W, srcRow1H),
            new RectangleF(srcX2, srcY1, srcCol2W, srcRow1H),
            new RectangleF(srcX0, srcY2, srcCol0W, srcRow2H),
            new RectangleF(srcX1, srcY2, srcCol1W, srcRow2H),
            new RectangleF(srcX2, srcY2, srcCol2W, srcRow2H),
            new RectangleF(dstX0, dstY0, dstCol0W, dstRow0H),
            new RectangleF(dstX1, dstY0, dstCol1W, dstRow0H),
            new RectangleF(dstX2, dstY0, dstCol2W, dstRow0H),
            new RectangleF(dstX0, dstY1, dstCol0W, dstRow1H),
            new RectangleF(dstX1, dstY1, dstCol1W, dstRow1H),
            new RectangleF(dstX2, dstY1, dstCol2W, dstRow1H),
            new RectangleF(dstX0, dstY2, dstCol0W, dstRow2H),
            new RectangleF(dstX1, dstY2, dstCol1W, dstRow2H),
            new RectangleF(dstX2, dstY2, dstCol2W, dstRow2H));
    }

    private NineSliceGrid(
        RectangleF s0, RectangleF s1, RectangleF s2,
        RectangleF s3, RectangleF s4, RectangleF s5,
        RectangleF s6, RectangleF s7, RectangleF s8,
        RectangleF d0, RectangleF d1, RectangleF d2,
        RectangleF d3, RectangleF d4, RectangleF d5,
        RectangleF d6, RectangleF d7, RectangleF d8)
    {
        this.SourceTopLeft = s0;
        this.SourceTopCenter = s1;
        this.SourceTopRight = s2;
        this.SourceCenterLeft = s3;
        this.SourceCenter = s4;
        this.SourceCenterRight = s5;
        this.SourceBottomLeft = s6;
        this.SourceBottomCenter = s7;
        this.SourceBottomRight = s8;

        this.DestTopLeft = d0;
        this.DestTopCenter = d1;
        this.DestTopRight = d2;
        this.DestCenterLeft = d3;
        this.DestCenter = d4;
        this.DestCenterRight = d5;
        this.DestBottomLeft = d6;
        this.DestBottomCenter = d7;
        this.DestBottomRight = d8;
    }
}
