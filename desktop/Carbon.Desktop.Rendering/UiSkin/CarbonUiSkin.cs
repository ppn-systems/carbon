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
/// Restrained dark-slate RPG UI palette eliminating high-saturation color clutter across HUD and modals.
/// </summary>
public static class CarbonUiPalette
{
    /// <summary>Primary modal backdrop fill.</summary>
    public static readonly ColorRgba WindowFill = new(13, 16, 23, 246);

    /// <summary>Secondary inner card fill.</summary>
    public static readonly ColorRgba CardFill = new(18, 22, 31, 238);

    /// <summary>Resting inventory/hotbar socket fill.</summary>
    public static readonly ColorRgba SlotFill = new(11, 14, 21, 242);

    /// <summary>Hovered inventory/hotbar socket fill.</summary>
    public static readonly ColorRgba SlotHoverFill = new(27, 34, 48, 248);

    /// <summary>Selected socket or active tab fill.</summary>
    public static readonly ColorRgba SlotSelectedFill = new(34, 42, 58, 250);

    /// <summary>Restrained steel outer frame border.</summary>
    public static readonly ColorRgba BorderOuter = new(114, 126, 144, 235);

    /// <summary>Subtle muted divider and slot border.</summary>
    public static readonly ColorRgba BorderSubtle = new(66, 76, 92, 220);

    /// <summary>Warm muted brass accent used sparingly on active focus/selection.</summary>
    public static readonly ColorRgba BorderActive = new(212, 186, 130, 255);

    /// <summary>Primary crisp off-white UI text.</summary>
    public static readonly ColorRgba TextPrimary = new(236, 240, 245, 255);

    /// <summary>Secondary muted slate UI text.</summary>
    public static readonly ColorRgba TextMuted = new(152, 163, 180, 255);

    /// <summary>Warm muted brass text accent.</summary>
    public static readonly ColorRgba TextWarm = new(224, 202, 152, 255);

    /// <summary>Dark outline stroke color for crisp bitmap text.</summary>
    public static readonly ColorRgba TextOutline = new(8, 10, 14, 255);

    /// <summary>Health bar fill.</summary>
    public static readonly ColorRgba HpFill = new(188, 48, 56, 255);

    /// <summary>Mana bar fill.</summary>
    public static readonly ColorRgba MpFill = new(52, 108, 194, 255);

    /// <summary>Stamina bar fill.</summary>
    public static readonly ColorRgba StaminaFill = new(58, 152, 104, 255);

    /// <summary>Hunger (food) bar fill while fed.</summary>
    public static readonly ColorRgba HungerFill = new(206, 138, 56, 255);

    /// <summary>Hunger bar fill while hungry or worse.</summary>
    public static readonly ColorRgba HungerWarnFill = new(222, 88, 40, 255);

    /// <summary>Rest bar fill while rested or tired.</summary>
    public static readonly ColorRgba RestFill = new(112, 104, 204, 255);

    /// <summary>Rest bar fill while very tired or exhausted.</summary>
    public static readonly ColorRgba RestWarnFill = new(168, 72, 160, 255);

    /// <summary>Hunger bar fill while food poisoned.</summary>
    public static readonly ColorRgba PoisonFill = new(128, 160, 52, 255);

    /// <summary>Warm XP bar fill.</summary>
    public static readonly ColorRgba XpFill = new(198, 168, 104, 255);

    /// <summary>Muted crimson fill for close/danger buttons.</summary>
    public static readonly ColorRgba DangerFill = new(126, 36, 42, 238);

    /// <summary>Muted crimson border for close/danger buttons.</summary>
    public static readonly ColorRgba DangerBorder = new(188, 76, 82, 240);

    /// <summary>Muted forest fill for confirm/positive buttons.</summary>
    public static readonly ColorRgba ConfirmFill = new(34, 96, 68, 240);

    /// <summary>Muted forest border for confirm/positive buttons.</summary>
    public static readonly ColorRgba ConfirmBorder = new(96, 168, 128, 245);
}

/// <summary>
/// Dual-layer nine-slice UI skin downsampling <c>96x96</c> pixel templates by <c>2x</c> to crisp <c>48x48</c>
/// textures and splitting each panel into a watertight solid interior body and crisp outer border filigree.
/// </summary>
public sealed class CarbonUiSkin : IDisposable
{
    private readonly Texture2D _atlas;
    private readonly PanelSkinEntry[] _entries;
    private bool _disposed;

    private CarbonUiSkin(Texture2D atlas, PanelSkinEntry[] entries)
    {
        _atlas = atlas;
        _entries = entries;
    }

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

        // Every style's body and frame live in one atlas texture (audit round 2, PERF-8): HUD panels drawn one after
        // another then share a texture, so the sprite batch no longer breaks its batch twice per panel. Each image is
        // decoded once even when several styles use it ("15.png" serves four).
        Dictionary<string, PanelImage> images = new(StringComparer.Ordinal);
        List<PanelImage> ordered = [];
        PanelImage[] perSpec = new PanelImage[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            if (!images.TryGetValue(specs[i].FileName, out PanelImage? image))
            {
                image = LoadPanelImage(panelDir, specs[i]);
                images[specs[i].FileName] = image;
                ordered.Add(image);
            }

            perSpec[i] = image;
        }

        PanelAtlasLayout atlasLayout = PanelAtlasLayout.Pack(ordered);
        Texture2D atlas = CreateTexture(concrete, (uint)atlasLayout.Width, (uint)atlasLayout.Height, atlasLayout.Pixels);
        PanelSkinEntry[] entries = new PanelSkinEntry[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            PanelImage image = perSpec[i];
            entries[i] = new PanelSkinEntry(image.BodySource, image.FrameSource, image.HasFile ? specs[i].Margins : new NineSliceMargins(4f));
        }

        return new CarbonUiSkin(atlas, entries);
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
    /// Truncates <paramref name="text"/> with <c>".."</c> if its measured width at <paramref name="scale"/>
    /// exceeds <paramref name="maxWidth"/>.
    /// </summary>
    public static string TruncateToWidth(BitmapFont font, string text, float maxWidth, float scale = 1f)
    {
        ArgumentNullException.ThrowIfNull(font);
        if (string.IsNullOrEmpty(text) || maxWidth <= 8f)
        {
            return string.Empty;
        }

        if (font.MeasureString(text, TextLayoutOptions.Default, scale).Width <= maxWidth)
        {
            return text;
        }

        // Same scan as before (longest prefix + ".." that fits, longest first), but each candidate is composed in
        // a reusable buffer and measured as a span, so only the final string is allocated. The old loop built a
        // string per step: O(n^2) characters per call for every over-long label, every frame (audit round 2, PERF-9).
        const string ellipsis = "..";
        int capacity = text.Length + 1;
        char[]? rented = null;
        Span<char> buffer = capacity <= 256
            ? stackalloc char[256]
            : (rented = System.Buffers.ArrayPool<char>.Shared.Rent(capacity));
        try
        {
            text.AsSpan().CopyTo(buffer);
            for (int len = text.Length - 1; len > 0; len--)
            {
                buffer[len] = '.';
                buffer[len + 1] = '.';
                ReadOnlySpan<char> candidate = buffer[..(len + ellipsis.Length)];
                if (font.MeasureString(candidate, TextLayoutOptions.Default, scale).Width <= maxWidth)
                {
                    return new string(candidate);
                }
            }
        }
        finally
        {
            if (rented is not null)
            {
                System.Buffers.ArrayPool<char>.Shared.Return(rented);
            }
        }

        return ellipsis;
    }

    /// <summary>
    /// Parses a <c>#RRGGBB</c> hex color string with a safe fallback when malformed.
    /// </summary>
    public static ColorRgba ParseHexColor(string? hex, ColorRgba fallback)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return fallback;
        }

        ReadOnlySpan<char> span = hex.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
        {
            span = span[1..];
        }

        if (span.Length == 6 &&
            byte.TryParse(span[..2], System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte r) &&
            byte.TryParse(span.Slice(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte g) &&
            byte.TryParse(span.Slice(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out byte b))
        {
            return new ColorRgba(r, g, b, 255);
        }

        return fallback;
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
        if (bodyColor.A > 0)
        {
            RectangleF bodySource = entry.BodySource;
            spriteBatch.DrawNineSlice(_atlas, in bodySource, in clampedMargins, in bounds, bodyColor);
        }

        if (frameColor.A > 0)
        {
            RectangleF frameSource = entry.FrameSource;
            spriteBatch.DrawNineSlice(_atlas, in frameSource, in clampedMargins, in bounds, frameColor);
        }
    }

    /// <summary>Draws an ornate dialog window backdrop and frame.</summary>
    public void DrawWindow(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba frameColor, byte bodyAlpha = 246) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.OrnateWindow, in bounds, new ColorRgba(CarbonUiPalette.WindowFill.R, CarbonUiPalette.WindowFill.G, CarbonUiPalette.WindowFill.B, bodyAlpha), frameColor);

    /// <summary>Draws a clean double-bevel card with obsidian dark tint.</summary>
    public void DrawCard(SpriteBatch spriteBatch, in RectangleF bounds, ColorRgba frameColor, byte bodyAlpha = 238) =>
        this.DrawPanel(spriteBatch, UiPanelStyle.DoubleCard, in bounds, new ColorRgba(CarbonUiPalette.CardFill.R, CarbonUiPalette.CardFill.G, CarbonUiPalette.CardFill.B, bodyAlpha), frameColor);

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

        _atlas.Dispose();
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

    private static PanelImage LoadPanelImage(string? panelDir, PanelSpec spec)
    {
        if (panelDir is not null)
        {
            string fullPath = Path.Combine(panelDir, spec.FileName);
            if (File.Exists(fullPath))
            {
                DecodedImage decoded = ImageDecoder.Decode(File.ReadAllBytes(fullPath));
                DownsampleAndSplit(decoded, out int outW, out int outH, out byte[] bodyRgba, out byte[] frameRgba);
                return new PanelImage(outW, outH, bodyRgba, frameRgba, HasFile: true);
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

        return new PanelImage(size, size, fallbackBody, fallbackFrame, HasFile: false);
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

    private readonly record struct PanelSkinEntry(RectangleF BodySource, RectangleF FrameSource, NineSliceMargins Margins);
}
