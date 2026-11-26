// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using Carbon.Graphics.Assets.Decoders;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
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
    /// <summary>Ornate corner-bracketed frame (<c>0.png</c>) for modal dialogs and top-level menus.</summary>
    OrnateWindow = 0,

    /// <summary>Double-line architectural card (<c>14.png</c>) for HUD panels and inspection panes.</summary>
    DoubleCard = 1,

    /// <summary>Trimmed square-corner bevel panel (<c>1.png</c>) for buttons and active tabs.</summary>
    Button = 2,

    /// <summary>Framed inventory/equipment socket (<c>9.png</c>) for hotbar, bag, armor, and recipe slots.</summary>
    Slot = 3,

    /// <summary>Crisp thin inset box (<c>15.png</c>) for text fields, tooltips, and badges.</summary>
    Inset = 4,

    /// <summary>Double-cornered ornate highlight frame (<c>4.png</c>) for featured boss/hero cards.</summary>
    OrnateHighlight = 5,
}

/// <summary>
/// Dual-layer nine-slice UI skin splitting each standardized monochrome pixel panel from
/// <c>resources/ui/panels/</c> into separate interior body and outer border textures so
/// translucent obsidian fills and crisp tinted gold/cyan/emerald/red filigree borders compose cleanly.
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
            new("0.png", new NineSliceMargins(20f)),
            new("14.png", new NineSliceMargins(20f)),
            new("1.png", new NineSliceMargins(14f)),
            new("9.png", new NineSliceMargins(16f)),
            new("15.png", new NineSliceMargins(12f)),
            new("4.png", new NineSliceMargins(20f)),
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
                if (File.Exists(Path.Combine(candidate, "0.png")))
                {
                    return candidate;
                }

                cursor = cursor.Parent;
            }
        }

        return null;
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
    public void DrawWindow(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba frameColor, byte bodyAlpha = 238) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.OrnateWindow, in bounds, new ColorRgba(10, 15, 28, bodyAlpha), frameColor);

    /// <summary>Draws a double-lined panel card with obsidian dark tint.</summary>
    public void DrawCard(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba frameColor, byte bodyAlpha = 228) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.DoubleCard, in bounds, new ColorRgba(12, 19, 34, bodyAlpha), frameColor);

    /// <summary>Draws an inventory or hotbar slot bezel.</summary>
    public void DrawSlot(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba fillColor, ColorRgba frameColor) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.Slot, in bounds, fillColor, frameColor);

    /// <summary>Draws a stylized RPG button frame.</summary>
    public void DrawButton(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba fillColor, ColorRgba frameColor) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.Button, in bounds, fillColor, frameColor);

    /// <summary>Draws a thin crisp inset panel (inputs, tooltips, pills).</summary>
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
        float maxH = MathF.Floor(MathF.Max(1f, (bounds.Width * 0.5f) - 1f));
        float maxV = MathF.Floor(MathF.Max(1f, (bounds.Height * 0.5f) - 1f));
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
                SplitBodyAndFrame(decoded, out byte[] bodyRgba, out byte[] frameRgba);
                return CreatePanelEntry(device, (uint)decoded.Width, (uint)decoded.Height, bodyRgba, frameRgba, spec.Margins);
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
                bool border = x <= 2 || x >= size - 3 || y <= 2 || y >= size - 3;
                byte[] target = border ? fallbackFrame : fallbackBody;
                target[i] = 255;
                target[i + 1] = 255;
                target[i + 2] = 255;
                target[i + 3] = 255;
            }
        }

        return CreatePanelEntry(device, size, size, fallbackBody, fallbackFrame, new NineSliceMargins(6f));
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

    private static void SplitBodyAndFrame(DecodedImage decoded, out byte[] bodyRgba, out byte[] frameRgba)
    {
        int w = decoded.Width;
        int h = decoded.Height;
        int pixelCount = w * h;
        ReadOnlySpan<byte> src = decoded.Pixels;

        bodyRgba = new byte[pixelCount * 4];
        frameRgba = new byte[pixelCount * 4];
        bool[] isBody = new bool[pixelCount];
        bool[] visited = new bool[pixelCount];

        int seedX = w / 2;
        int seedY = h / 2;
        int seedIdx = (seedY * w) + seedX;
        if (src[(seedIdx * 4) + 3] > 128)
        {
            Queue<int> queue = new(pixelCount / 2);
            visited[seedIdx] = true;
            isBody[seedIdx] = true;
            queue.Enqueue(seedIdx);

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                int cx = cur % w;
                int cy = cur / w;

                CheckNeighbor(cx - 1, cy, w, h, src, visited, isBody, queue);
                CheckNeighbor(cx + 1, cy, w, h, src, visited, isBody, queue);
                CheckNeighbor(cx, cy - 1, w, h, src, visited, isBody, queue);
                CheckNeighbor(cx, cy + 1, w, h, src, visited, isBody, queue);
            }
        }

        for (int p = 0; p < pixelCount; p++)
        {
            int o = p * 4;
            byte a = src[o + 3];
            if (a == 0)
            {
                continue;
            }

            byte[] dst = isBody[p] ? bodyRgba : frameRgba;
            dst[o] = src[o];
            dst[o + 1] = src[o + 1];
            dst[o + 2] = src[o + 2];
            dst[o + 3] = a;
        }
    }

    private static void CheckNeighbor(
        int nx,
        int ny,
        int w,
        int h,
        ReadOnlySpan<byte> src,
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
        Texture2D tex = new(
            device.Gl,
            device.StateCache,
            width,
            height,
            TextureFormat.Rgba8,
            TextureFilter.Nearest,
            TextureWrap.ClampToEdge);
        tex.SetData(rgba);
        return tex;
    }

    private readonly record struct PanelSpec(string FileName, NineSliceMargins Margins);

    private readonly record struct PanelSkinEntry(
        Texture2D Body,
        Texture2D Frame,
        RectangleF SourceBounds,
        NineSliceMargins Margins);
}
