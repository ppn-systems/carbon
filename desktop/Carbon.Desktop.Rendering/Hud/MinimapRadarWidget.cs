// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Globalization;
using System.Numerics;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Device;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Minimap in the right-hand HUD column (<see cref="HudLayout"/>, under the clock and clear of the status-effect strip).
/// Terrain comes from <see cref="MinimapTerrainCache"/>: one GL atlas texel per tile, a chunk is baked when it first
/// enters the view and re-baked only when the session reports it dirty. Markers come from
/// <see cref="MinimapIconCollector"/> (villages, traders, blacksmiths, players and monsters already in the AoI). The
/// player is a facing arrow at the centre, north is marked on the top edge, and the mouse wheel over the map zooms.
/// Steady-state frames allocate nothing: buffers are reused and text is formatted into stack spans.
/// </summary>
internal sealed class MinimapRadarWidget : IMinimapTextureSink, IDisposable
{
    /// <summary>Chunk bakes allowed per frame, so a teleport spreads its work over a few frames.</summary>
    internal const int MaxBakesPerFrame = 3;

    private const int MaxIcons = 160;

    private static readonly ColorRgba s_mapBackground = new(14, 20, 30, 255);
    private static readonly ColorRgba s_mapBorder = new(148, 163, 184, 235);
    private static readonly ColorRgba s_playerFill = new(255, 255, 255, 255);
    private static readonly ColorRgba s_arrowOutline = new(15, 23, 42, 255);
    private static readonly ColorRgba s_otherPlayer = new(56, 189, 248, 255);
    private static readonly ColorRgba s_monster = new(239, 68, 68, 255);
    private static readonly ColorRgba s_npc = new(250, 204, 21, 255);
    private static readonly ColorRgba s_target = new(250, 204, 21, 255);
    private static readonly ColorRgba s_northBadge = new(15, 23, 42, 240);

    private readonly MinimapTerrainCache _cache = new();
    private readonly MinimapIcon[] _icons = new MinimapIcon[MaxIcons];
    private readonly GraphicsDevice? _device;
    private Texture2D? _atlas;
    private ChunkWorld2D? _boundWorld;
    private IVectorArtRepository? _boundArt;
    private MinimapView _view;
    private int _iconCount;
    private int _zoomLevel = MinimapView.DefaultZoomLevel;
    private bool _disposed;

    public MinimapRadarWidget(IGraphicsDevice device) => _device = device as GraphicsDevice;

    /// <summary>Gets the current zoom preset (<c>0</c> is the closest).</summary>
    public int ZoomLevel => _zoomLevel;

    /// <summary>Gets the terrain cache (tests and diagnostics).</summary>
    internal MinimapTerrainCache Cache => _cache;

    /// <summary>Re-bakes chunk <c>(chunkX, chunkY)</c> the next time it is visible (terrain changed).</summary>
    public void InvalidateChunk(int chunkX, int chunkY) => _cache.Invalidate(chunkX, chunkY);

    /// <summary>Returns whether <paramref name="mousePos"/> is over the map.</summary>
    public static bool ContainsPoint(Vector2 mousePos, int viewportWidth) => HudLayout.GetMinimapMapRect(viewportWidth).Contains(mousePos);

    /// <summary>Zooms in (<paramref name="steps"/> &gt; 0) or out when the cursor is over the map.</summary>
    /// <returns>Whether the map consumed the wheel.</returns>
    public bool TryZoom(Vector2 mousePos, int steps, int viewportWidth)
    {
        if (steps == 0 || !ContainsPoint(mousePos, viewportWidth))
        {
            return false;
        }

        _zoomLevel = Math.Clamp(_zoomLevel - steps, 0, MinimapView.ZoomLevelCount - 1);
        return true;
    }

    /// <inheritdoc/>
    void IMinimapTextureSink.UploadChunk(int slotX, int slotY, ReadOnlySpan<byte> rgba) =>
        _atlas?.SetData(rgba, slotX * Chunk2D.Size, slotY * Chunk2D.Size, Chunk2D.Size, Chunk2D.Size);

    /// <summary>Pass 1: frame, map background and the cached terrain chunks.</summary>
    public void DrawSkin(SpriteBatch spriteBatch, CarbonUiSkin skin, IWorldReadModel world, IVectorArtRepository? art, int viewportWidth)
    {
        ArgumentNullException.ThrowIfNull(world);
        RectangleF frame = HudLayout.GetMinimapFrameRect(viewportWidth);
        RectangleF map = HudLayout.GetMinimapMapRect(viewportWidth);
        skin.DrawCard(spriteBatch, in frame, CarbonUiPalette.BorderOuter, bodyAlpha: 236);
        skin.DrawInset(spriteBatch, in map, s_mapBackground, CarbonUiPalette.BorderSubtle);

        ChunkWorld2D chunkWorld = world.ChunkWorld;
        if (!ReferenceEquals(chunkWorld, _boundWorld) || !ReferenceEquals(art, _boundArt))
        {
            _cache.Clear();
            _boundWorld = chunkWorld;
            _boundArt = art;
        }

        _view = MinimapView.Create(world.LocalPlayerInterpolatedPosition, chunkWorld.TileSize, _zoomLevel, map);
        this.EnsureAtlas();
        if (_atlas is null)
        {
            return;
        }

        (int MinX, int MinY, int MaxX, int MaxY) range = _view.VisibleChunks();
        _ = _cache.Prepare(chunkWorld, art, range, this, MaxBakesPerFrame);

        float minTx = _view.MinTileX;
        float minTy = _view.MinTileY;
        float maxTx = minTx + (_view.RadiusTiles * 2f);
        float maxTy = minTy + (_view.RadiusTiles * 2f);
        float ppt = _view.PixelsPerTile;
        for (int cy = range.MinY; cy <= range.MaxY; cy++)
        {
            for (int cx = range.MinX; cx <= range.MaxX; cx++)
            {
                if (!_cache.TryGetSlot(cx, cy, out int slotX, out int slotY))
                {
                    continue;
                }

                float tx0 = cx * Chunk2D.Size;
                float ty0 = cy * Chunk2D.Size;
                float x0 = MathF.Max(tx0, minTx);
                float y0 = MathF.Max(ty0, minTy);
                float x1 = MathF.Min(tx0 + Chunk2D.Size, maxTx);
                float y1 = MathF.Min(ty0 + Chunk2D.Size, maxTy);
                if (x1 <= x0 || y1 <= y0)
                {
                    continue;
                }

                RectangleF source = new((slotX * Chunk2D.Size) + (x0 - tx0), (slotY * Chunk2D.Size) + (y0 - ty0), x1 - x0, y1 - y0);
                spriteBatch.Draw(_atlas, _view.Project(x0, y0), source, ColorRgba.White, 0f, Vector2.Zero, new Vector2(ppt, ppt));
            }
        }
    }

    /// <summary>Pass 2: markers, the player's facing arrow, the north badge and the map border.</summary>
    public void DrawGeometry(GeometryBatch batch, IWorldReadModel world, IVectorArtRepository? art, int viewportWidth)
    {
        ArgumentNullException.ThrowIfNull(world);
        RectangleF map = HudLayout.GetMinimapMapRect(viewportWidth);
        _iconCount = MinimapIconCollector.Collect(world.ChunkWorld, world.RemoteEntities, world.LockedTargetNetworkId, in _view, world.ChunkWorld.TileSize, _icons);

        CompiledItemVectorArt? village = null;
        CompiledItemVectorArt? trader = null;
        CompiledItemVectorArt? smith = null;
        _ = art?.TryGetUiIcon("minimap_village", out village);
        _ = art?.TryGetUiIcon("minimap_trader", out trader);
        _ = art?.TryGetUiIcon("minimap_smith", out smith);

        for (int i = 0; i < _iconCount; i++)
        {
            MinimapIcon icon = _icons[i];
            switch (icon.Kind)
            {
                case MinimapIconKind.Village:
                    DrawArtOrDot(batch, village, icon.Position, 15f, s_npc);
                    break;
                case MinimapIconKind.Trader:
                    DrawArtOrDot(batch, trader, icon.Position, 10f, s_npc);
                    break;
                case MinimapIconKind.Blacksmith:
                    DrawArtOrDot(batch, smith, icon.Position, 10f, s_npc);
                    break;
                case MinimapIconKind.Npc:
                    batch.FillCircle(icon.Position, 2.2f, s_npc, 8);
                    break;
                case MinimapIconKind.Player:
                    batch.FillCircle(icon.Position, 3.2f, s_arrowOutline, 10);
                    batch.FillCircle(icon.Position, 2.4f, s_otherPlayer, 10);
                    break;
                case MinimapIconKind.Monster:
                default:
                    batch.FillCircle(icon.Position, 2.8f, s_arrowOutline, 8);
                    batch.FillCircle(icon.Position, 2.1f, s_monster, 8);
                    break;
            }

            if (icon.IsLockedTarget)
            {
                batch.DrawCircle(icon.Position, 5f, s_target, 12, 1.2f);
            }
        }

        DrawPlayerArrow(batch, map.Center, world.LocalFacingDirection);

        // North badge on the top edge (the map never rotates: north is always up).
        Vector2 north = new(map.X + (map.Width * 0.5f), map.Y + 1f);
        batch.FillCircle(north, 8f, s_northBadge, 14);
        batch.DrawCircle(north, 8f, CarbonUiPalette.TextWarm, 14, 1.2f);
        batch.DrawRectangle(in map, s_mapBorder, 1f);
    }

    /// <summary>Pass 3: the north letter and the footer (coordinates, or the zoom hint while hovered).</summary>
    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, int viewportWidth, int ptx, int pty, Vector2? mouseScreenPos)
    {
        RectangleF map = HudLayout.GetMinimapMapRect(viewportWidth);
        RectangleF frame = HudLayout.GetMinimapFrameRect(viewportWidth);
        ColorRgba outline = CarbonUiPalette.TextOutline;
        RectangleF northBox = new(map.X + (map.Width * 0.5f) - 8f, map.Y - 7f, 16f, 16f);
        CarbonUiSkin.DrawCenteredText(spriteBatch, font, GameText.Get("minimap.north"), in northBox, CarbonUiPalette.TextWarm, outline);

        Vector2 footer = new(map.X, map.Bottom + 3f);
        if (mouseScreenPos is Vector2 mouse && map.Contains(mouse))
        {
            string hint = GameText.Get("minimap.zoom_hint");
            TextMeasureResult size = font.MeasureString(hint, TextLayoutOptions.Default, 1f);
            if (size.Width <= frame.Width - 8f)
            {
                spriteBatch.DrawOutlinedString(font, hint, footer, CarbonUiPalette.TextMuted, outline, 1, 1f);
                return;
            }
        }

        Span<char> coords = stackalloc char[32];
        spriteBatch.DrawOutlinedString(font, FormatCoordinates(ptx, pty, coords), footer, CarbonUiPalette.TextMuted, outline, 1, 1f);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _atlas?.Dispose();
            _atlas = null;
            _disposed = true;
        }
    }

    /// <summary>Formats <c>(x, y)</c> into <paramref name="buffer"/> without allocating.</summary>
    internal static ReadOnlySpan<char> FormatCoordinates(int x, int y, Span<char> buffer)
    {
        int n = 0;
        buffer[n++] = '(';
        _ = x.TryFormat(buffer[n..], out int wx, provider: CultureInfo.InvariantCulture);
        n += wx;
        buffer[n++] = ',';
        buffer[n++] = ' ';
        _ = y.TryFormat(buffer[n..], out int wy, provider: CultureInfo.InvariantCulture);
        n += wy;
        buffer[n++] = ')';
        return buffer[..n];
    }

    /// <summary>Returns the three corners of the player arrow (tip first) for a facing direction; south when unknown.</summary>
    internal static (Vector2 Tip, Vector2 Left, Vector2 Right) GetArrow(Vector2 center, Vector2 facing)
    {
        Vector2 d = facing.LengthSquared() > 1e-6f ? Vector2.Normalize(facing) : new Vector2(0f, 1f);
        Vector2 side = new(-d.Y, d.X);
        return (center + (d * 7f), center - (d * 4f) + (side * 4.6f), center - (d * 4f) - (side * 4.6f));
    }

    private static void DrawPlayerArrow(GeometryBatch batch, Vector2 center, Vector2 facing)
    {
        (Vector2 tip, Vector2 left, Vector2 right) = GetArrow(center, facing);
        const float outlineGrow = 1.35f;
        batch.FillTriangle(
            center + ((tip - center) * outlineGrow),
            center + ((left - center) * outlineGrow),
            center + ((right - center) * outlineGrow),
            s_arrowOutline);
        batch.FillTriangle(tip, left, right, s_playerFill);
    }

    private static void DrawArtOrDot(GeometryBatch batch, CompiledItemVectorArt? art, Vector2 position, float size, ColorRgba fallback)
    {
        if (art is not null)
        {
            VectorRigRenderer.DrawItemIcon(batch, art, position, size);
        }
        else
        {
            batch.FillCircle(position, 3f, fallback, 10);
        }
    }

    private void EnsureAtlas()
    {
        if (_atlas is not null || _device is null || _disposed)
        {
            return;
        }

        _atlas = new Texture2D(
            _device.Gl,
            _device.StateCache,
            MinimapTerrainCache.AtlasSize,
            MinimapTerrainCache.AtlasSize,
            TextureFormat.Rgba8,
            TextureFilter.Nearest,
            TextureWrap.ClampToEdge);
        _cache.Clear();
    }
}
