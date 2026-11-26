// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Carbon.Graphics.Assets.Decoders;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Graphics.UI.NineSlice;

namespace Carbon.Desktop.Rendering.UiSkin;

/// <summary>
/// Semantic nine-slice panel silhouettes loaded from <c>resources/ui/panels/</c>.
/// </summary>
public enum UiPanelStyle
{
    /// <summary>Ornate corner-bracketed frame (<c>10.png</c>, downsampled 2x) for top-level dialogs.</summary>
    OrnateWindow = 0,

    /// <summary>Clean crisp bevel card (<c>15.png</c>, downsampled 2x) for HUD panels and inspection panes.</summary>
    DoubleCard = 1,

    /// <summary>Clean bevel button (<c>15.png</c>, downsampled 2x) with unobstructed interior for centered text.</summary>
    Button = 2,

    /// <summary>Clean crisp equipment/hotbar/bag socket (<c>15.png</c>, downsampled 2x).</summary>
    Slot = 3,

    /// <summary>Crisp thin inset box (<c>15.png</c>, downsampled 2x) for text fields, tooltips, and badges.</summary>
    Inset = 4,

    /// <summary>Ornate highlight frame (<c>5.png</c>, downsampled 2x) for featured boss/target cards.</summary>
    OrnateHighlight = 5,
}

/// <summary>
/// Dual-layer nine-slice UI skin downsampling <c>96x96</c> pixel templates by <c>2x</c> to crisp <c>48x48</c>
/// textures and splitting each panel into a watertight solid interior body and crisp outer border filigree.
/// </summary>
public sealed class CarbonUiSkin : IDisposable
{
    private readonly PanelSkinEntry[] _entries;
    private bool _disposed;

    private CarbonUiSkin(PanelSkinEntry[] entries) => _entries = entries;

    /// <summary>
    /// Searches for <c>resources/ui/panels</c> and uploads dual-layer nine-slice textures onto <paramref name="device"/>.
    /// Falls back cleanly to a procedural bevel frame if external panel PNGs are unavailable.
    /// </summary>
    public static CarbonUiSkin Load(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device is not GraphicsDevice concrete)
        {
            throw new ArgumentException("Requires concrete GraphicsDevice to upload UI skin textures.", nameof(device));
        }

        string? panelDir = TryFindPanelDirectory();
        PanelSpec[] specs =
        [
            new("10.png", new NineSliceMargins(10f)),
            new("15.png", new NineSliceMargins(5f)),
            new("15.png", new NineSliceMargins(4f)),
            new("15.png", new NineSliceMargins(4f)),
            new("15.png", new NineSliceMargins(4f)),
            new("5.png", new NineSliceMargins(10f)),
        ];

        PanelSkinEntry[] entries = new PanelSkinEntry[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            entries[i] = LoadPanelEntry(concrete, panelDir, specs[i]);
        }

        return new CarbonUiSkin(entries);
    }

    /// <summary>Searches standard runtime directories for <c>resources/ui/panels</c>.</summary>
    public static string? TryFindPanelDirectory()
    {
        string[] roots = [AppContext.BaseDirectory, Directory.GetCurrentDirectory()];
        for (int i = 0; i < roots.Length; i++)
        {
            DirectoryInfo? cursor = new(roots[i]);
            while (cursor is not null)
            {
                string candidate = Path.Combine(cursor.FullName, "resources", "ui", "panels");
                if (File.Exists(Path.Combine(candidate, "15.png")))
                {
                    return candidate;
                }

                cursor = cursor.Parent;
            }
        }

        return null;
    }

    /// <summary>
    /// Draws <paramref name="text"/> centered cleanly inside <paramref name="bounds"/> with a crisp 1px dark outline.
    /// </summary>
    public static void DrawCenteredText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        string text,
        in RectangleF bounds,
        ColorRgba color,
        ColorRgba outline,
        float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(spriteBatch);
        ArgumentNullException.ThrowIfNull(font);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        TextMeasureResult m = font.MeasureString(text, TextLayoutOptions.Default, scale);
        float tx = MathF.Floor(bounds.X + ((bounds.Width - m.Width) * 0.5f));
        float ty = MathF.Floor(bounds.Y + ((bounds.Height - m.Height) * 0.5f));
        spriteBatch.DrawOutlinedString(font, text, new Vector2(tx, ty), color, outline, 1, scale);
    }

    /// <summary>
    /// Renders a dual-layer nine-slice panel with independently tinted <paramref name="bodyColor"/>
    /// interior fill and <paramref name="frameColor"/> border filigree.
    /// </summary>
    public void DrawPanel(
        SpriteBatch spriteBatch,
        UiPanelStyle style,
        in RectangleF bounds,
        ColorRgba bodyColor,
        ColorRgba frameColor)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(spriteBatch);

        int idx = (int)style;
        if ((uint)idx >= (uint)_entries.Length || bounds.Width <= 2f || bounds.Height <= 2f)
        {
            return;
        }

        PanelSkinEntry entry = _entries[idx];
        NineSliceMargins clampedMargins = ClampMargins(entry.Margins, bounds);
        RectangleF sourceBounds = entry.SourceBounds;
        if (bodyColor.A > 0)
        {
            spriteBatch.DrawNineSlice(entry.Body, in sourceBounds, in clampedMargins, in bounds, bodyColor);
        }

        if (frameColor.A > 0)
        {
            spriteBatch.DrawNineSlice(entry.Frame, in sourceBounds, in clampedMargins, in bounds, frameColor);
        }
    }

    /// <summary>Draws an ornate dialog window backdrop and frame.</summary>
    public void DrawWindow(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba frameColor, byte bodyAlpha = 242) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.OrnateWindow, in bounds, new ColorRgba(10, 15, 28, bodyAlpha), frameColor);

    /// <summary>Draws a clean double-bevel card with obsidian dark tint.</summary>
    public void DrawCard(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba frameColor, byte bodyAlpha = 232) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.DoubleCard, in bounds, new ColorRgba(12, 19, 34, bodyAlpha), frameColor);

    /// <summary>Draws an inventory or hotbar slot bezel.</summary>
    public void DrawSlot(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba fillColor, ColorRgba frameColor) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.Slot, in bounds, fillColor, frameColor);

    /// <summary>Draws a stylized RPG button frame.</summary>
    public void DrawButton(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba fillColor, ColorRgba frameColor) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.Button, in bounds, fillColor, frameColor);

    /// <summary>Draws a thin crisp inset panel (inputs, tooltips, badges).</summary>
    public void DrawInset(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba fillColor, ColorRgba frameColor) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.Inset, in bounds, fillColor, frameColor);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (int i = 0; i < _entries.Length; i++)
        {
            _entries[i].Body.Dispose();
            _entries[i].Frame.Dispose();
        }

        _disposed = true;
    }

    private static NineSliceMargins ClampMargins(NineSliceMargins source, in RectangleF bounds)
    {
        float maxH = MathF.Floor(MathF.Max(1f, (bounds.Width * 0.45f) - 1f));
        float maxV = MathF.Floor(MathF.Max(1f, (bounds.Height * 0.45f) - 1f));
        return new NineSliceMargins(
            MathF.Min(source.Left, maxH),
            MathF.Min(source.Top, maxV),
            MathF.Min(source.Right, maxH),
            MathF.Min(source.Bottom, maxV));
    }

    private static PanelSkinEntry LoadPanelEntry(GraphicsDevice device, string? panelDir, PanelSpec spec)
    {
        if (panelDir is not null)
        {
            string fullPath = Path.Combine(panelDir, spec.FileName);
            if (File.Exists(fullPath))
            {
                DecodedImage decoded = ImageDecoder.Decode(File.ReadAllBytes(fullPath));
                DownsampleAndSplit(decoded, out int outW, out int outH, out byte[] bodyRgba, out byte[] frameRgba);
                return CreatePanelEntry(device, (uint)outW, (uint)outH, bodyRgba, frameRgba, spec.Margins);
            }
        }

        // Procedural fallback if external assets are absent
        const int size = 32;
        byte[] fallbackBody = new byte[size * size * 4];
        byte[] fallbackFrame = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = ((y * size) + x) * 4;
                bool border = x <= 1 || x >= size - 2 || y <= 1 || y >= size - 2;
                byte[] target = border ? fallbackFrame : fallbackBody;
                target[i] = 255;
                target[i + 1] = 255;
                target[i + 2] = 255;
                target[i + 3] = 255;
            }
        }

        return CreatePanelEntry(device, size, size, fallbackBody, fallbackFrame, new NineSliceMargins(4f));
    }

    private static PanelSkinEntry CreatePanelEntry(
        GraphicsDevice device,
        uint width,
        uint height,
        byte[] bodyRgba,
        byte[] frameRgba,
        NineSliceMargins margins)
    {
        Texture2D? bodyTex = null;
        Texture2D? frameTex = null;
        try
        {
            bodyTex = CreateTexture(device, width, height, bodyRgba);
            frameTex = CreateTexture(device, width, height, frameRgba);
            PanelSkinEntry entry = new(bodyTex, frameTex, new RectangleF(0f, 0f, width, height), margins);
            bodyTex = null;
            frameTex = null;
            return entry;
        }
        finally
        {
            frameTex?.Dispose();
            bodyTex?.Dispose();
        }
    }

    private static void DownsampleAndSplit(
        DecodedImage decoded,
        out int outW,
        out int outH,
        out byte[] bodyRgba,
        out byte[] frameRgba)
    {
        int factor = decoded.Width >= 96 && decoded.Height >= 96 ? 2 : 1;
        outW = decoded.Width / factor;
        outH = decoded.Height / factor;
        int pixelCount = outW * outH;
        ReadOnlySpan<byte> raw = decoded.Pixels;

        byte[] dsRgba = new byte[pixelCount * 4];
        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                int srcIdx = ((y * factor * decoded.Width) + (x * factor)) * 4;
                int dstIdx = ((y * outW) + x) * 4;
                dsRgba[dstIdx] = raw[srcIdx];
                dsRgba[dstIdx + 1] = raw[srcIdx + 1];
                dsRgba[dstIdx + 2] = raw[srcIdx + 2];
                dsRgba[dstIdx + 3] = raw[srcIdx + 3];
            }
        }

        bodyRgba = new byte[pixelCount * 4];
        frameRgba = new byte[pixelCount * 4];
        bool[] isCenterBody = new bool[pixelCount];
        bool[] visited = new bool[pixelCount];

        int seedX = outW / 2;
        int seedY = outH / 2;
        int seedIdx = (seedY * outW) + seedX;
        if (dsRgba[(seedIdx * 4) + 3] > 128)
        {
            Queue<int> queue = new(pixelCount / 2);
            visited[seedIdx] = true;
            isCenterBody[seedIdx] = true;
            queue.Enqueue(seedIdx);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % outW;
                int cy = cur / outW;

                CheckNeighbor(cx - 1, cy, outW, outH, dsRgba, visited, isCenterBody, queue);
                CheckNeighbor(cx + 1, cy, outW, outH, dsRgba, visited, isCenterBody, queue);
                CheckNeighbor(cx, cy - 1, outW, outH, dsRgba, visited, isCenterBody, queue);
                CheckNeighbor(cx, cy + 1, outW, outH, dsRgba, visited, isCenterBody, queue);
            }
        }

        // Track min/max opaque span per row & column so enclosed transparent moats inside borders stay solid-filled!
        int[] rowMin = new int[outH];
        int[] rowMax = new int[outH];
        int[] colMin = new int[outW];
        int[] colMax = new int[outW];
        Array.Fill(rowMin, int.MaxValue);
        Array.Fill(rowMax, -1);
        Array.Fill(colMin, int.MaxValue);
        Array.Fill(colMax, -1);

        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                if (dsRgba[(((y * outW) + x) * 4) + 3] > 128)
                {
                    if (x < rowMin[y])
                    {
                        rowMin[y] = x;
                    }

                    if (x > rowMax[y])
                    {
                        rowMax[y] = x;
                    }

                    if (y < colMin[x])
                    {
                        colMin[x] = y;
                    }

                    if (y > colMax[x])
                    {
                        colMax[x] = y;
                    }
                }
            }
        }

        for (int y = 0; y < outH; y++)
        {
            for (int x = 0; x < outW; x++)
            {
                int p = (y * outW) + x;
                int o = p * 4;
                byte a = dsRgba[o + 3];
                bool enclosed = x > rowMin[y] && x < rowMax[y] && y > colMin[x] && y < colMax[x];

                if (isCenterBody[p] || (a == 0 && enclosed))
                {
                    bodyRgba[o] = 255;
                    bodyRgba[o + 1] = 255;
                    bodyRgba[o + 2] = 255;
                    bodyRgba[o + 3] = 255;
                }
                else if (a > 0)
                {
                    frameRgba[o] = dsRgba[o];
                    frameRgba[o + 1] = dsRgba[o + 1];
                    frameRgba[o + 2] = dsRgba[o + 2];
                    frameRgba[o + 3] = a;
                }
            }
        }
    }

    private static void CheckNeighbor(
        int nx,
        int ny,
        int w,
        int h,
        byte[] src,
        bool[] visited,
        bool[] isBody,
        Queue<int> queue)
    {
        if ((uint)nx >= (uint)w || (uint)ny >= (uint)h)
        {
            return;
        }

        int nIdx = (ny * w) + nx;
        if (visited[nIdx])
        {
            return;
        }

        visited[nIdx] = true;
        if (src[(nIdx * 4) + 3] > 128)
        {
            isBody[nIdx] = true;
            queue.Enqueue(nIdx);
        }
    }

    private static Texture2D CreateTexture(GraphicsDevice device, uint width, uint height, byte[] rgba)
    {
        Texture2D? tex = null;
        try
        {
            tex = new Texture2D(
                device.Gl,
                device.StateCache,
                width,
                height,
                TextureFormat.Rgba8,
                TextureFilter.Nearest,
                TextureWrap.ClampToEdge);
            tex.SetData(rgba);
            Texture2D owned = tex;
            tex = null;
            return owned;
        }
        finally
        {
            tex?.Dispose();
        }
    }

    private readonly record struct PanelSpec(string FileName, NineSliceMargins Margins);

    private readonly record struct PanelSkinEntry(
        Texture2D Body,
        Texture2D Frame,
        RectangleF SourceBounds,
        NineSliceMargins Margins);
}
