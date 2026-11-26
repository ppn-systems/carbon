// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Fonts.Atlases;
using Carbon.Graphics.Fonts.Model;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.Textures;

namespace Carbon.Graphics.Fonts.TrueType;

/// <summary>
/// Rasterized glyph metadata produced by <see cref="TrueTypeFontLoader"/> prior to GPU upload.
/// </summary>
/// <param name="CodePoint">Unicode code point.</param>
/// <param name="Bounds">Atlas texture rectangle in pixels.</param>
/// <param name="XOffset">Horizontal bearing offset in pixels.</param>
/// <param name="YOffset">Vertical bearing offset relative to line top in pixels.</param>
/// <param name="XAdvance">Horizontal cursor advance in pixels.</param>
public readonly record struct RasterizedGlyphInfo(
    int CodePoint,
    RectangleF Bounds,
    float XOffset,
    float YOffset,
    float XAdvance);

/// <summary>
/// CPU-rasterized RGBA8 font atlas and layout metrics decoded from a TrueType (<c>.ttf</c>) font file.
/// </summary>
public sealed class TrueTypeRasterizedFont
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TrueTypeRasterizedFont"/> class.
    /// </summary>
    /// <param name="name">Friendly font name.</param>
    /// <param name="atlasWidth">Packed atlas width in pixels.</param>
    /// <param name="atlasHeight">Packed atlas height in pixels.</param>
    /// <param name="rgbaPixels">Packed RGBA8 bitmap buffer.</param>
    /// <param name="metrics">Font-wide layout metrics.</param>
    /// <param name="glyphs">Rasterized printable glyph entries.</param>
    public TrueTypeRasterizedFont(
        string name,
        uint atlasWidth,
        uint atlasHeight,
        byte[] rgbaPixels,
        in FontMetrics metrics,
        IReadOnlyList<RasterizedGlyphInfo> glyphs)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentNullException.ThrowIfNull(rgbaPixels);
        ArgumentNullException.ThrowIfNull(glyphs);

        this.Name = name;
        this.AtlasWidth = atlasWidth;
        this.AtlasHeight = atlasHeight;
        this.RgbaPixels = rgbaPixels;
        this.Metrics = metrics;
        this.Glyphs = glyphs;
    }

    /// <summary>Gets the font name.</summary>
    public string Name { get; }

    /// <summary>Gets the atlas texture width in pixels.</summary>
    public uint AtlasWidth { get; }

    /// <summary>Gets the atlas texture height in pixels.</summary>
    public uint AtlasHeight { get; }

    /// <summary>Gets the RGBA8 bitmap buffer.</summary>
    public byte[] RgbaPixels { get; }

    /// <summary>Gets the decoded font metrics.</summary>
    public FontMetrics Metrics { get; }

    /// <summary>Gets the packed glyph entries.</summary>
    public IReadOnlyList<RasterizedGlyphInfo> Glyphs { get; }
}

/// <summary>
/// Zero-dependency, Native-AOT-safe TrueType (<c>.ttf</c>) ASCII rasterizer producing packed RGBA8 glyph atlases
/// compatible with <see cref="BitmapFont"/> without requiring external native font libraries.
/// </summary>
public static class TrueTypeFontLoader
{
    private const int CellPadding = 2;
    private const int AtlasWidth = 256;

    /// <summary>
    /// Parses a TrueType font payload and rasterizes printable ASCII (<c>32..126</c>) glyphs at <paramref name="pixelSize"/>.
    /// </summary>
    /// <param name="name">Friendly font identifier.</param>
    /// <param name="ttfData">Raw bytes of the <c>.ttf</c> file.</param>
    /// <param name="pixelSize">Target pixel height.</param>
    /// <returns>CPU-side packed atlas pixels and glyph metrics.</returns>
    public static TrueTypeRasterizedFont RasterizeAscii(string name, ReadOnlySpan<byte> ttfData, float pixelSize = 16f)
    {
        ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelSize, nameof(pixelSize));

        if (ttfData.Length < 12)
        {
            throw new InvalidOperationException("TrueType font data is truncated.");
        }

        ushort numTables = ReadU16(ttfData, 4);
        int headOff = FindTable(ttfData, numTables, "head");
        int hheaOff = FindTable(ttfData, numTables, "hhea");
        int maxpOff = FindTable(ttfData, numTables, "maxp");
        int cmapOff = FindTable(ttfData, numTables, "cmap");
        int locaOff = FindTable(ttfData, numTables, "loca");
        int glyfOff = FindTable(ttfData, numTables, "glyf");
        int hmtxOff = FindTable(ttfData, numTables, "hmtx");

        if (headOff < 0 || hheaOff < 0 || maxpOff < 0 || cmapOff < 0 || locaOff < 0 || glyfOff < 0 || hmtxOff < 0)
        {
            throw new InvalidOperationException("TrueType font is missing required outline tables (head/hhea/maxp/cmap/loca/glyf/hmtx).");
        }

        ushort unitsPerEm = Math.Max((ushort)1, ReadU16(ttfData, headOff + 18));
        short indexToLocFormat = ReadS16(ttfData, headOff + 50);
        short ascent = ReadS16(ttfData, hheaOff + 4);
        short descent = ReadS16(ttfData, hheaOff + 6);
        short lineGap = ReadS16(ttfData, hheaOff + 8);
        ushort numHMetrics = Math.Max((ushort)1, ReadU16(ttfData, hheaOff + 34));
        ushort numGlyphs = ReadU16(ttfData, maxpOff + 4);

        int cmapFormat4Off = FindCmapFormat4Subtable(ttfData, cmapOff);
        int cmapFormat0Off = cmapFormat4Off < 0 ? FindCmapFormat0Subtable(ttfData, cmapOff) : -1;
        if (cmapFormat4Off < 0 && cmapFormat0Off < 0)
        {
            throw new InvalidOperationException("TrueType font lacks a supported cmap encoding subtable (format 4 or format 0).");
        }

        float scale = pixelSize / unitsPerEm;
        float baselinePx = MathF.Round(ascent * scale);
        float lineHeightPx = MathF.Ceiling((ascent - descent + lineGap) * scale);
        if (lineHeightPx < pixelSize)
        {
            lineHeightPx = pixelSize;
        }

        int spaceGlyphId = MapCodePoint(ttfData, cmapFormat4Off, cmapFormat0Off, ' ');
        float spaceAdvance = ReadAdvanceWidth(ttfData, hmtxOff, numHMetrics, spaceGlyphId) * scale;
        if (spaceAdvance <= 0.5f)
        {
            spaceAdvance = MathF.Round(pixelSize * 0.35f);
        }

        List<GlyphBitmap> renderedGlyphs = new(95);
        List<EdgeSegment> edgeScratch = new(64);
        List<int> endPointScratch = new(8);
        List<byte> flagScratch = new(64);
        List<Vector2> pointScratch = new(64);

        for (int cp = 32; cp <= 126; cp++)
        {
            int glyphId = MapCodePoint(ttfData, cmapFormat4Off, cmapFormat0Off, cp);
            if (glyphId == 0 && cp != ' ')
            {
                continue;
            }

            float advance = ReadAdvanceWidth(ttfData, hmtxOff, numHMetrics, glyphId) * scale;
            if (cp == ' ' || !TryBuildGlyphEdges(
                ttfData,
                locaOff,
                glyfOff,
                indexToLocFormat,
                numGlyphs,
                glyphId,
                Matrix3x2.Identity,
                edgeScratch,
                endPointScratch,
                flagScratch,
                pointScratch,
                out short xMin,
                out short yMin,
                out short xMax,
                out short yMax))
            {
                renderedGlyphs.Add(new GlyphBitmap(cp, 0, 0, 0f, 0f, advance, []));
                continue;
            }

            int gx0 = (int)MathF.Floor(xMin * scale);
            int gx1 = (int)MathF.Ceiling(xMax * scale);
            int gy0 = (int)MathF.Floor(yMin * scale);
            int gy1 = (int)MathF.Ceiling(yMax * scale);
            int gw = Math.Max(1, gx1 - gx0);
            int gh = Math.Max(1, gy1 - gy0);

            byte[] alpha = RasterizeEdgesToAlpha(edgeScratch, gx0, gy1, gw, gh, scale);
            float xOffset = gx0;
            float yOffset = baselinePx - gy1;
            renderedGlyphs.Add(new GlyphBitmap(cp, gw, gh, xOffset, yOffset, MathF.Max(1f, advance), alpha));
        }

        // Pack into power-of-two atlas height with 2px cell margins
        int cursorX = CellPadding;
        int cursorY = CellPadding;
        int rowHeight = 0;
        for (int i = 0; i < renderedGlyphs.Count; i++)
        {
            GlyphBitmap g = renderedGlyphs[i];
            if (g.Width <= 0 || g.Height <= 0)
            {
                continue;
            }

            if (cursorX + g.Width + CellPadding > AtlasWidth)
            {
                cursorX = CellPadding;
                cursorY += rowHeight + CellPadding;
                rowHeight = 0;
            }

            cursorX += g.Width + CellPadding;
            if (g.Height > rowHeight)
            {
                rowHeight = g.Height;
            }
        }

        int requiredHeight = cursorY + rowHeight + CellPadding;
        int atlasHeight = 64;
        while (atlasHeight < requiredHeight)
        {
            atlasHeight *= 2;
        }

        byte[] atlasPixels = new byte[AtlasWidth * atlasHeight * 4];
        List<RasterizedGlyphInfo> glyphInfos = new(renderedGlyphs.Count);

        cursorX = CellPadding;
        cursorY = CellPadding;
        rowHeight = 0;
        for (int i = 0; i < renderedGlyphs.Count; i++)
        {
            GlyphBitmap g = renderedGlyphs[i];
            if (g.Width <= 0 || g.Height <= 0)
            {
                glyphInfos.Add(new RasterizedGlyphInfo(g.CodePoint, new RectangleF(0f, 0f, 0f, 0f), 0f, 0f, g.XAdvance));
                continue;
            }

            if (cursorX + g.Width + CellPadding > AtlasWidth)
            {
                cursorX = CellPadding;
                cursorY += rowHeight + CellPadding;
                rowHeight = 0;
            }

            for (int py = 0; py < g.Height; py++)
            {
                for (int px = 0; px < g.Width; px++)
                {
                    byte cov = g.Alpha[(py * g.Width) + px];
                    if (cov == 0)
                    {
                        continue;
                    }

                    int dstIdx = (((cursorY + py) * AtlasWidth) + cursorX + px) * 4;
                    atlasPixels[dstIdx] = 255;
                    atlasPixels[dstIdx + 1] = 255;
                    atlasPixels[dstIdx + 2] = 255;
                    atlasPixels[dstIdx + 3] = cov;
                }
            }

            RectangleF bounds = new(cursorX, cursorY, g.Width, g.Height);
            glyphInfos.Add(new RasterizedGlyphInfo(g.CodePoint, bounds, g.XOffset, g.YOffset, g.XAdvance));

            cursorX += g.Width + CellPadding;
            if (g.Height > rowHeight)
            {
                rowHeight = g.Height;
            }
        }

        FontMetrics metrics = new(pixelSize, lineHeightPx, baselinePx, spaceAdvance, spaceAdvance * 4f);
        return new TrueTypeRasterizedFont(name, AtlasWidth, (uint)atlasHeight, atlasPixels, in metrics, glyphInfos);
    }

    /// <summary>
    /// Rasterizes a TrueType font payload and uploads the packed atlas onto <paramref name="device"/>.
    /// </summary>
    /// <param name="name">Friendly font identifier.</param>
    /// <param name="device">Active graphics device.</param>
    /// <param name="ttfData">Raw bytes of the <c>.ttf</c> file.</param>
    /// <param name="pixelSize">Target pixel height.</param>
    /// <returns>GPU-backed <see cref="BitmapFont"/>.</returns>
    public static BitmapFont CreateFromTrueType(string name, IGraphicsDevice device, ReadOnlySpan<byte> ttfData, float pixelSize = 16f)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device is not GraphicsDevice concrete)
        {
            throw new ArgumentException("Requires concrete GraphicsDevice to upload TrueType font atlas.", nameof(device));
        }

        TrueTypeRasterizedFont rasterized = RasterizeAscii(name, ttfData, pixelSize);
        Texture2D? texture = null;
        BitmapFont? font = null;
        try
        {
            texture = new Texture2D(
                concrete.Gl,
                concrete.StateCache,
                rasterized.AtlasWidth,
                rasterized.AtlasHeight,
                TextureFormat.Rgba8,
                TextureFilter.Nearest,
                TextureWrap.ClampToEdge);
            texture.SetData(rasterized.RgbaPixels);

            BitmapFontAtlas atlas = new(texture);
            texture = null;

            for (int i = 0; i < rasterized.Glyphs.Count; i++)
            {
                RasterizedGlyphInfo g = rasterized.Glyphs[i];
                RectangleF rect = g.Bounds;
                FontGlyph glyph = new(g.CodePoint, in rect, g.XOffset, g.YOffset, g.XAdvance);
                atlas.AddGlyph(glyph);
                if (g.CodePoint == '?')
                {
                    atlas.FallbackGlyph = glyph;
                }
            }

            FontMetrics m = rasterized.Metrics;
            font = new BitmapFont(rasterized.Name, atlas, in m);
            BitmapFont result = font;
            font = null;
            return result;
        }
        finally
        {
            font?.Dispose();
            texture?.Dispose();
        }
    }

    private static int FindTable(ReadOnlySpan<byte> data, ushort numTables, string targetTag)
    {
        Span<byte> tagBytes = stackalloc byte[4];
        for (int i = 0; i < numTables; i++)
        {
            int entryOff = 12 + (i * 16);
            if (entryOff + 16 > data.Length)
            {
                break;
            }

            data.Slice(entryOff, 4).CopyTo(tagBytes);
            string tag = Encoding.ASCII.GetString(tagBytes);
            if (string.Equals(tag, targetTag, StringComparison.Ordinal))
            {
                int offset = (int)ReadU32(data, entryOff + 8);
                return offset >= 0 && offset < data.Length ? offset : -1;
            }
        }

        return -1;
    }

    private static int FindCmapFormat4Subtable(ReadOnlySpan<byte> data, int cmapOff)
    {
        if (cmapOff + 4 > data.Length)
        {
            return -1;
        }

        ushort numSubtables = ReadU16(data, cmapOff + 2);
        int fallbackFmt4 = -1;
        for (int i = 0; i < numSubtables; i++)
        {
            int recOff = cmapOff + 4 + (i * 8);
            if (recOff + 8 > data.Length)
            {
                break;
            }

            ushort platformId = ReadU16(data, recOff);
            ushort encodingId = ReadU16(data, recOff + 2);
            int subOff = cmapOff + (int)ReadU32(data, recOff + 4);
            if (subOff + 2 > data.Length)
            {
                continue;
            }

            if (ReadU16(data, subOff) == 4)
            {
                if ((platformId == 3 && encodingId == 1) || (platformId == 0 && encodingId == 3))
                {
                    return subOff;
                }

                if (fallbackFmt4 < 0)
                {
                    fallbackFmt4 = subOff;
                }
            }
        }

        return fallbackFmt4;
    }

    private static int FindCmapFormat0Subtable(ReadOnlySpan<byte> data, int cmapOff)
    {
        if (cmapOff + 4 > data.Length)
        {
            return -1;
        }

        ushort numSubtables = ReadU16(data, cmapOff + 2);
        for (int i = 0; i < numSubtables; i++)
        {
            int recOff = cmapOff + 4 + (i * 8);
            if (recOff + 8 > data.Length)
            {
                break;
            }

            int subOff = cmapOff + (int)ReadU32(data, recOff + 4);
            if (subOff + 262 <= data.Length && ReadU16(data, subOff) == 0)
            {
                return subOff;
            }
        }

        return -1;
    }

    private static int MapCodePoint(ReadOnlySpan<byte> data, int fmt4Off, int fmt0Off, int codePoint)
    {
        if (fmt4Off >= 0 && fmt4Off + 14 <= data.Length)
        {
            int segCount = ReadU16(data, fmt4Off + 6) / 2;
            int endBase = fmt4Off + 14;
            int startBase = endBase + (segCount * 2) + 2;
            int deltaBase = startBase + (segCount * 2);
            int rangeBase = deltaBase + (segCount * 2);

            for (int i = 0; i < segCount; i++)
            {
                int endOff = endBase + (i * 2);
                if (rangeBase + (i * 2) + 2 > data.Length)
                {
                    break;
                }

                ushort endCode = ReadU16(data, endOff);
                if (codePoint > endCode)
                {
                    continue;
                }

                ushort startCode = ReadU16(data, startBase + (i * 2));
                if (codePoint < startCode)
                {
                    return 0;
                }

                short idDelta = ReadS16(data, deltaBase + (i * 2));
                ushort idRangeOffset = ReadU16(data, rangeBase + (i * 2));
                if (idRangeOffset == 0)
                {
                    return (codePoint + idDelta) & 0xFFFF;
                }

                int glyphAddr = rangeBase + (i * 2) + idRangeOffset + ((codePoint - startCode) * 2);
                if (glyphAddr + 2 > data.Length)
                {
                    return 0;
                }

                ushort glyphId = ReadU16(data, glyphAddr);
                return glyphId == 0 ? 0 : (glyphId + idDelta) & 0xFFFF;
            }

            return 0;
        }

        if (fmt0Off >= 0 && codePoint is >= 0 and < 256 && fmt0Off + 6 + codePoint < data.Length)
        {
            return data[fmt0Off + 6 + codePoint];
        }

        return 0;
    }

    private static ushort ReadAdvanceWidth(ReadOnlySpan<byte> data, int hmtxOff, ushort numHMetrics, int glyphId)
    {
        int metricIndex = glyphId < numHMetrics ? glyphId : Math.Max(0, numHMetrics - 1);
        int off = hmtxOff + (metricIndex * 4);
        return off + 2 <= data.Length ? ReadU16(data, off) : (ushort)0;
    }

    private static bool TryBuildGlyphEdges(
        ReadOnlySpan<byte> data,
        int locaOff,
        int glyfOff,
        short indexToLocFormat,
        ushort numGlyphs,
        int glyphId,
        Matrix3x2 transform,
        List<EdgeSegment> edges,
        List<int> endPoints,
        List<byte> flags,
        List<Vector2> points,
        out short xMin,
        out short yMin,
        out short xMax,
        out short yMax,
        int depth = 0)
    {
        if (depth == 0)
        {
            edges.Clear();
        }

        xMin = yMin = xMax = yMax = 0;
        if (glyphId < 0 || glyphId >= numGlyphs || depth > 4)
        {
            return false;
        }

        int gStart;
        int gEnd;
        if (indexToLocFormat == 0)
        {
            int lAddr = locaOff + (glyphId * 2);
            if (lAddr + 4 > data.Length)
            {
                return false;
            }

            gStart = ReadU16(data, lAddr) * 2;
            gEnd = ReadU16(data, lAddr + 2) * 2;
        }
        else
        {
            int lAddr = locaOff + (glyphId * 4);
            if (lAddr + 8 > data.Length)
            {
                return false;
            }

            gStart = (int)ReadU32(data, lAddr);
            gEnd = (int)ReadU32(data, lAddr + 4);
        }

        if (gStart >= gEnd || glyfOff + gStart + 10 > data.Length)
        {
            return false;
        }

        int go = glyfOff + gStart;
        short numberOfContours = ReadS16(data, go);
        xMin = ReadS16(data, go + 2);
        yMin = ReadS16(data, go + 4);
        xMax = ReadS16(data, go + 6);
        yMax = ReadS16(data, go + 8);

        if (numberOfContours < 0)
        {
            return AppendCompoundGlyphEdges(
                data,
                locaOff,
                glyfOff,
                indexToLocFormat,
                numGlyphs,
                go + 10,
                transform,
                edges,
                depth);
        }

        if (numberOfContours == 0)
        {
            return false;
        }

        endPoints.Clear();
        int endPtsOff = go + 10;
        for (int c = 0; c < numberOfContours; c++)
        {
            endPoints.Add(ReadU16(data, endPtsOff + (c * 2)));
        }

        int numPoints = endPoints[^1] + 1;
        int insLen = ReadU16(data, endPtsOff + (numberOfContours * 2));
        int fp = endPtsOff + (numberOfContours * 2) + 2 + insLen;
        if (fp > data.Length)
        {
            return false;
        }

        flags.Clear();
        while (flags.Count < numPoints && fp < data.Length)
        {
            byte fl = data[fp++];
            flags.Add(fl);
            if ((fl & 0x08) != 0 && fp < data.Length)
            {
                byte rep = data[fp++];
                for (int r = 0; r < rep && flags.Count < numPoints; r++)
                {
                    flags.Add(fl);
                }
            }
        }

        if (flags.Count < numPoints)
        {
            return false;
        }

        int[] xs = new int[numPoints];
        int cx = 0;
        for (int i = 0; i < numPoints; i++)
        {
            byte fl = flags[i];
            if ((fl & 0x02) != 0)
            {
                int dx = fp < data.Length ? data[fp++] : 0;
                if ((fl & 0x10) == 0)
                {
                    dx = -dx;
                }

                cx += dx;
            }
            else if ((fl & 0x10) == 0)
            {
                int dx = fp + 2 <= data.Length ? ReadS16(data, fp) : 0;
                fp += 2;
                cx += dx;
            }

            xs[i] = cx;
        }

        points.Clear();
        int cy = 0;
        for (int i = 0; i < numPoints; i++)
        {
            byte fl = flags[i];
            if ((fl & 0x04) != 0)
            {
                int dy = fp < data.Length ? data[fp++] : 0;
                if ((fl & 0x20) == 0)
                {
                    dy = -dy;
                }

                cy += dy;
            }
            else if ((fl & 0x20) == 0)
            {
                int dy = fp + 2 <= data.Length ? ReadS16(data, fp) : 0;
                fp += 2;
                cy += dy;
            }

            points.Add(Vector2.Transform(new Vector2(xs[i], cy), transform));
        }

        int contourStart = 0;
        for (int c = 0; c < numberOfContours; c++)
        {
            int contourEnd = endPoints[c];
            AppendContourEdges(points, flags, contourStart, contourEnd, edges);
            contourStart = contourEnd + 1;
        }

        return edges.Count > 0;
    }

    private static bool AppendCompoundGlyphEdges(
        ReadOnlySpan<byte> data,
        int locaOff,
        int glyfOff,
        short indexToLocFormat,
        ushort numGlyphs,
        int offset,
        Matrix3x2 parentTransform,
        List<EdgeSegment> edges,
        int depth)
    {
        const ushort arg1And2AreWords = 0x0001;
        const ushort argsAreXyValues = 0x0002;
        const ushort weHaveAScale = 0x0008;
        const ushort moreComponents = 0x0020;
        const ushort weHaveAnXAndYScale = 0x0040;
        const ushort weHaveATwoByTwo = 0x0080;

        bool anyAdded = false;
        int cursor = offset;
        while (cursor + 4 <= data.Length)
        {
            ushort compFlags = ReadU16(data, cursor);
            ushort compGlyphId = ReadU16(data, cursor + 2);
            cursor += 4;

            float tx = 0f;
            float ty = 0f;
            if ((compFlags & arg1And2AreWords) != 0)
            {
                if (cursor + 4 > data.Length)
                {
                    break;
                }

                if ((compFlags & argsAreXyValues) != 0)
                {
                    tx = ReadS16(data, cursor);
                    ty = ReadS16(data, cursor + 2);
                }

                cursor += 4;
            }
            else
            {
                if (cursor + 2 > data.Length)
                {
                    break;
                }

                if ((compFlags & argsAreXyValues) != 0)
                {
                    tx = (sbyte)data[cursor];
                    ty = (sbyte)data[cursor + 1];
                }

                cursor += 2;
            }

            float m11 = 1f;
            float m12 = 0f;
            float m21 = 0f;
            float m22 = 1f;
            if ((compFlags & weHaveAScale) != 0 && cursor + 2 <= data.Length)
            {
                float s = ReadF2Dot14(data, cursor);
                m11 = s;
                m22 = s;
                cursor += 2;
            }
            else if ((compFlags & weHaveAnXAndYScale) != 0 && cursor + 4 <= data.Length)
            {
                m11 = ReadF2Dot14(data, cursor);
                m22 = ReadF2Dot14(data, cursor + 2);
                cursor += 4;
            }
            else if ((compFlags & weHaveATwoByTwo) != 0 && cursor + 8 <= data.Length)
            {
                m11 = ReadF2Dot14(data, cursor);
                m12 = ReadF2Dot14(data, cursor + 2);
                m21 = ReadF2Dot14(data, cursor + 4);
                m22 = ReadF2Dot14(data, cursor + 6);
                cursor += 8;
            }

            Matrix3x2 local = new(m11, m12, m21, m22, tx, ty);
            Matrix3x2 combined = local * parentTransform;

            List<int> subEnds = [];
            List<byte> subFlags = [];
            List<Vector2> subPts = [];
            if (TryBuildGlyphEdges(
                data,
                locaOff,
                glyfOff,
                indexToLocFormat,
                numGlyphs,
                compGlyphId,
                combined,
                edges,
                subEnds,
                subFlags,
                subPts,
                out _,
                out _,
                out _,
                out _,
                depth + 1))
            {
                anyAdded = true;
            }

            if ((compFlags & moreComponents) == 0)
            {
                break;
            }
        }

        return anyAdded;
    }

    private static void AppendContourEdges(
        List<Vector2> points,
        List<byte> flags,
        int startIdx,
        int endIdx,
        List<EdgeSegment> edges)
    {
        int count = endIdx - startIdx + 1;
        if (count < 2)
        {
            return;
        }

        List<Vector2> flattened = new(count * 2);
        Vector2 firstPt = points[startIdx];
        bool firstOn = (flags[startIdx] & 1) != 0;

        Vector2 prevPt;
        int iterStart;
        if (firstOn)
        {
            prevPt = firstPt;
            iterStart = 1;
            flattened.Add(prevPt);
        }
        else
        {
            Vector2 lastPt = points[endIdx];
            bool lastOn = (flags[endIdx] & 1) != 0;
            prevPt = lastOn ? lastPt : (lastPt + firstPt) * 0.5f;
            iterStart = 0;
            flattened.Add(prevPt);
        }

        for (int k = iterStart; k < count; k++)
        {
            int idx = startIdx + k;
            Vector2 p = points[idx];
            bool onCurve = (flags[idx] & 1) != 0;
            if (onCurve)
            {
                flattened.Add(p);
                prevPt = p;
            }
            else
            {
                int nextIdx = startIdx + ((k + 1) % count);
                Vector2 next = points[nextIdx];
                bool nextOn = (flags[nextIdx] & 1) != 0;
                Vector2 end = nextOn ? next : (p + next) * 0.5f;
                FlattenQuadraticBezier(prevPt, p, end, flattened);
                prevPt = end;
                if (nextOn && k + 1 < count)
                {
                    k++;
                }
            }
        }

        if (flattened.Count >= 2)
        {
            for (int i = 0; i < flattened.Count; i++)
            {
                Vector2 a = flattened[i];
                Vector2 b = flattened[(i + 1) % flattened.Count];
                if (MathF.Abs(a.Y - b.Y) > 1e-4f)
                {
                    edges.Add(new EdgeSegment(a, b));
                }
            }
        }
    }

    private static void FlattenQuadraticBezier(Vector2 p0, Vector2 ctrl, Vector2 p2, List<Vector2> output)
    {
        const int steps = 4;
        for (int s = 1; s <= steps; s++)
        {
            float t = s / (float)steps;
            float inv = 1f - t;
            Vector2 pt = (inv * inv * p0) + (2f * inv * t * ctrl) + (t * t * p2);
            output.Add(pt);
        }
    }

    private static byte[] RasterizeEdgesToAlpha(
        List<EdgeSegment> edges,
        int gx0,
        int gy1,
        int width,
        int height,
        float scale)
    {
        byte[] alpha = new byte[width * height];
        const int subSamples = 3;
        float invScale = 1f / scale;
        const float subStep = 1f / subSamples;
        const float halfSub = subStep * 0.5f;

        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                int hits = 0;
                for (int sy = 0; sy < subSamples; sy++)
                {
                    float sampleFontY = (gy1 - (py + (sy * subStep) + halfSub)) * invScale;
                    for (int sx = 0; sx < subSamples; sx++)
                    {
                        float sampleFontX = (gx0 + px + (sx * subStep) + halfSub) * invScale;
                        if (ComputeWinding(edges, sampleFontX, sampleFontY) != 0)
                        {
                            hits++;
                        }
                    }
                }

                if (hits > 0)
                {
                    int cov = hits * 255 / (subSamples * subSamples);
                    alpha[(py * width) + px] = cov >= 245 ? (byte)255 : (byte)cov;
                }
            }
        }

        return alpha;
    }

    private static int ComputeWinding(List<EdgeSegment> edges, float px, float py)
    {
        int winding = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            EdgeSegment e = edges[i];
            if (e.A.Y <= py)
            {
                if (e.B.Y > py && IsLeft(e.A, e.B, px, py) > 0f)
                {
                    winding++;
                }
            }
            else if (e.B.Y <= py && IsLeft(e.A, e.B, px, py) < 0f)
            {
                winding--;
            }
        }

        return winding;
    }

    private static float IsLeft(Vector2 a, Vector2 b, float px, float py) =>
        ((b.X - a.X) * (py - a.Y)) - ((px - a.X) * (b.Y - a.Y));

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

    private static short ReadS16(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));

    private static float ReadF2Dot14(ReadOnlySpan<byte> data, int offset) =>
        ReadS16(data, offset) / 16384f;

    private readonly record struct EdgeSegment(Vector2 A, Vector2 B);

    private readonly record struct GlyphBitmap(
        int CodePoint,
        int Width,
        int Height,
        float XOffset,
        float YOffset,
        float XAdvance,
        byte[] Alpha);
}
