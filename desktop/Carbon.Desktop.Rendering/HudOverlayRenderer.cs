// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Graphics.UI.Controls;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Crafting;
using Carbon.Items.Definitions;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Zero-asset screen-space MMORPG HUD overlay: character vitals bar (<see cref="ProgressBar"/>),
/// live radar minimap, 9-slot procedural item hotbar with stack &amp; durability indicators,
/// biome/clock telemetry, and toggleable crafting menu rendered at crisp 2x integer pixel scale.
/// </summary>
public sealed class HudOverlayRenderer : IDisposable
{
    private readonly GeometryBatch _geometryBatch;
    private readonly BitmapFont _font;
    private readonly Texture2D _whitePixel;
    private readonly ProgressBar _healthBar;
    private float _smoothedFps = 60f;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HudOverlayRenderer"/> class.
    /// </summary>
    /// <param name="device">Active OpenGL graphics device.</param>
    public HudOverlayRenderer(IGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _geometryBatch = new GeometryBatch(device);
        (_font, _whitePixel) = EmbeddedPixelFont.Create(device);
        _healthBar = new ProgressBar(new RectangleF(26f, 46f, 356f, 22f), 100f, 100f)
        {
            BackgroundTexture = _whitePixel,
            BackgroundSource = new RectangleF(0f, 0f, 1f, 1f),
            FillTexture = _whitePixel,
            FillSource = new RectangleF(0f, 0f, 1f, 1f),
            FillColor = new ColorRgba(220, 38, 38, 255),
            GhostColor = new ColorRgba(250, 204, 21, 230),
            BackgroundColor = new ColorRgba(15, 23, 42, 235),
            ShowText = false,
            Font = _font,
        };
    }

    /// <summary>Gets the embedded bitmap pixel font shared with world-space nameplates &amp; floating combat text.</summary>
    public BitmapFont Font => _font;

    /// <summary>Gets or sets whether the crafting panel is expanded.</summary>
    public bool ShowCraftingPanel { get; set; }

    /// <summary>Gets or sets whether the HUD overlay is visible.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Hit-tests screen-space pointer coordinates against the 9 hotbar slots.
    /// </summary>
    /// <param name="mousePos">Pointer position in logical screen pixels.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="slotIndex">Resolved zero-based hotbar slot index (<c>0..8</c>).</param>
    /// <returns><see langword="true"/> if a hotbar slot was hit; otherwise <see langword="false"/>.</returns>
    public bool TryHitTestHotbar(Vector2 mousePos, int viewportWidth, int viewportHeight, out byte slotIndex)
    {
        slotIndex = 0;
        if (!this.Visible || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        const int slotCount = 9;
        const float slotSize = 48f;
        const float gap = 5f;
        const float totalW = (slotCount * slotSize) + ((slotCount - 1) * gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - slotSize - 36f;

        if (mousePos.Y < startY || mousePos.Y > startY + slotSize)
        {
            return false;
        }

        for (int i = 0; i < slotCount; i++)
        {
            float sx = startX + (i * (slotSize + gap));
            if (mousePos.X >= sx && mousePos.X <= sx + slotSize)
            {
                slotIndex = (byte)i;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Hit-tests screen-space pointer coordinates against rows inside the workbench crafting panel.
    /// </summary>
    /// <param name="mousePos">Pointer position in logical screen pixels.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="recipeId">Resolved starter recipe identifier.</param>
    /// <returns><see langword="true"/> if a recipe row was hit; otherwise <see langword="false"/>.</returns>
    public bool TryHitTestCraftingRecipe(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort recipeId)
    {
        recipeId = 0;
        if (!this.Visible || !this.ShowCraftingPanel || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        const float width = 560f;
        const float height = 210f;
        float x = MathF.Floor((viewportWidth - width) * 0.5f);
        float y = viewportHeight - height - 102f;

        if (mousePos.X < x + 12f || mousePos.X > x + width - 12f)
        {
            return false;
        }

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        for (int i = 0; i < recipes.Length; i++)
        {
            float rowTop = y + 36f + (i * 26f);
            if (mousePos.Y >= rowTop && mousePos.Y < rowTop + 26f)
            {
                recipeId = (ushort)recipes[i].Id;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether <paramref name="mousePos"/> lies inside the expanded crafting workbench panel bounds.
    /// </summary>
    public bool IsPointInCraftingPanel(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        if (!this.Visible || !this.ShowCraftingPanel || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        const float width = 560f;
        const float height = 210f;
        float x = MathF.Floor((viewportWidth - width) * 0.5f);
        float y = viewportHeight - height - 102f;

        return mousePos.X >= x && mousePos.X <= x + width && mousePos.Y >= y && mousePos.Y <= y + height;
    }

    /// <summary>
    /// Renders all screen-space HUD widgets for the current frame.
    /// </summary>
    public void Render(
        IClientWorldSession session,
        SpriteBatch spriteBatch,
        int viewportWidth,
        int viewportHeight,
        float frameDt,
        float hourOfDay)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(spriteBatch);
        if (!this.Visible || !session.IsReady || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        if (frameDt > 0.0001f)
        {
            float instFps = Math.Clamp(1f / frameDt, 1f, 999f);
            _smoothedFps += (instFps - _smoothedFps) * 0.08f;
        }

        _healthBar.MaxValue = MathF.Max(1f, session.LocalPlayerMaxHealth);
        _healthBar.Value = Math.Clamp(session.LocalPlayerHealth, 0f, _healthBar.MaxValue);
        _healthBar.Update(frameDt);

        Matrix4x4 screenProj = Matrix4x4.CreateOrthographicOffCenter(0f, viewportWidth, viewportHeight, 0f, -1f, 1f);
        Vector2 playerPos = session.LocalPlayerInterpolatedPosition;
        float tileSize = session.ChunkWorld.TileSize;
        int ptx = (int)MathF.Floor(playerPos.X / tileSize);
        int pty = (int)MathF.Floor(playerPos.Y / tileSize);
        MapTile tile = session.ChunkWorld.GetTileAtWorld(ptx, pty);
        ItemSlot activeItem = session.Inventory.GetSlot(session.SelectedHotbarSlot);

        // 1. Vector panels, minimap tiles, hotbar frames & icons
        _geometryBatch.Begin(screenProj);
        DrawVitalsPanelGeometry(_geometryBatch, session);
        DrawMinimap(_geometryBatch, session, viewportWidth, ptx, pty);
        DrawHotbarGeometry(_geometryBatch, _font, session, viewportWidth, viewportHeight);
        if (this.ShowCraftingPanel)
        {
            DrawCraftingPanelGeometry(_geometryBatch, viewportWidth, viewportHeight);
        }

        _geometryBatch.End();

        // 2. ProgressBar + crisp 2x integer pixel-snapped text
        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
        _healthBar.Draw(spriteBatch);

        DrawVitalsText(spriteBatch, _font, session, ptx, pty, tile.Biome, activeItem.ItemId, hourOfDay, _smoothedFps);
        DrawMinimapHeaderText(spriteBatch, _font, viewportWidth, ptx, pty);
        DrawHotbarText(spriteBatch, _font, session, viewportWidth, viewportHeight);
        if (this.ShowCraftingPanel)
        {
            DrawCraftingPanelText(spriteBatch, _font, session.Inventory, viewportWidth, viewportHeight);
        }

        spriteBatch.End();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _geometryBatch.Dispose();
            _font.Dispose();
            _whitePixel.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    private static void DrawVitalsPanelGeometry(GeometryBatch batch, IClientWorldSession session)
    {
        RectangleF card = new(14f, 14f, 380f, 114f);
        batch.FillRectangle(card, new ColorRgba(10, 15, 28, 215));
        batch.DrawRectangle(card, new ColorRgba(71, 85, 105, 230), 1.5f);

        // Outer border around the health bar
        batch.DrawRectangle(new RectangleF(25f, 45f, 358f, 24f), new ColorRgba(100, 116, 139, 220), 1f);

        ColorRgba badgeColor = session.IsUdpActive
            ? new ColorRgba(16, 185, 129, 235)
            : (session.IsConnected ? new ColorRgba(37, 99, 235, 235) : new ColorRgba(185, 28, 28, 235));
        batch.FillRectangle(new RectangleF(288f, 20f, 94f, 20f), badgeColor);
    }

    private static void DrawMinimap(GeometryBatch batch, IClientWorldSession session, int viewportWidth, int ptx, int pty)
    {
        const float mapSize = 156f;
        const float pad = 6f;
        float mapX = viewportWidth - mapSize - 20f;
        float mapY = 40f;

        RectangleF outer = new(mapX - pad, 14f, mapSize + (pad * 2f), mapSize + 32f);
        batch.FillRectangle(outer, new ColorRgba(10, 15, 28, 220));
        batch.DrawRectangle(outer, new ColorRgba(148, 163, 184, 225), 1.5f);

        const int cells = 34;
        const int tileStep = 2;
        const float cellPx = mapSize / cells;
        int halfRange = cells * tileStep / 2;
        ChunkWorld2D world = session.ChunkWorld;

        for (int cy = 0; cy < cells; cy++)
        {
            int wy = pty - halfRange + (cy * tileStep);
            for (int cx = 0; cx < cells; cx++)
            {
                int wx = ptx - halfRange + (cx * tileStep);
                MapTile t = world.PeekTileAtWorld(wx, wy);
                ColorRgba color = world.IsRoadAtWorld(wx, wy)
                    ? new ColorRgba(180, 150, 110, 255)
                    : ResolveMinimapBiomeColor(t.Biome);
                batch.FillRectangle(new RectangleF(mapX + (cx * cellPx), mapY + (cy * cellPx), cellPx + 0.3f, cellPx + 0.3f), color);
            }
        }

        IReadOnlyList<RemoteEntityView> actors = session.RemoteEntities;
        float scale = cellPx / tileStep;
        float centerPxX = mapX + (mapSize * 0.5f);
        float centerPxY = mapY + (mapSize * 0.5f);
        float tileSize = world.TileSize;

        for (int i = 0; i < actors.Count; i++)
        {
            RemoteEntityView actor = actors[i];
            float dxTiles = (actor.Position.X / tileSize) - ptx;
            float dyTiles = (actor.Position.Y / tileSize) - pty;
            float ax = centerPxX + (dxTiles * scale);
            float ay = centerPxY + (dyTiles * scale);
            if (ax < mapX || ax > mapX + mapSize || ay < mapY || ay > mapY + mapSize)
            {
                continue;
            }

            ColorRgba dotColor = actor.Kind == EntityKind.Monster
                ? new ColorRgba(239, 68, 68, 255)
                : actor.Kind == EntityKind.Npc
                    ? new ColorRgba(250, 204, 21, 255)
                    : new ColorRgba(52, 211, 153, 255);
            batch.FillCircle(new Vector2(ax, ay), 2.8f, dotColor, 8);
        }

        batch.FillCircle(new Vector2(centerPxX, centerPxY), 3.6f, new ColorRgba(255, 255, 255, 255), 10);
        batch.DrawCircle(new Vector2(centerPxX, centerPxY), 4.5f, new ColorRgba(15, 23, 42, 255), 10, 1f);
    }

    private static void DrawHotbarGeometry(
        GeometryBatch batch,
        BitmapFont font,
        IClientWorldSession session,
        int viewportWidth,
        int viewportHeight)
    {
        const int slotCount = 9;
        const float slotSize = 48f;
        const float gap = 5f;
        float totalW = (slotCount * slotSize) + ((slotCount - 1) * gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - slotSize - 36f;

        batch.FillRectangle(new RectangleF(startX - 6f, startY - 6f, totalW + 12f, slotSize + 12f), new ColorRgba(10, 15, 28, 220));
        InventoryContainer inv = session.Inventory;

        for (int i = 0; i < slotCount; i++)
        {
            float sx = startX + (i * (slotSize + gap));
            RectangleF rect = new(sx, startY, slotSize, slotSize);
            bool active = i == session.SelectedHotbarSlot;

            batch.FillRectangle(rect, active ? new ColorRgba(30, 41, 59, 240) : new ColorRgba(15, 23, 42, 225));
            batch.DrawRectangle(
                rect,
                active ? new ColorRgba(250, 204, 21, 255) : new ColorRgba(71, 85, 105, 225),
                active ? 2.5f : 1f);

            ItemSlot item = inv.GetSlot(i);
            if (!item.IsEmpty)
            {
                DrawItemIcon(batch, item.ItemId, sx + (slotSize * 0.5f), startY + (slotSize * 0.5f));
                if (item.Durability > 0f && StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? archetype) && archetype.MaxDurability > 0f)
                {
                    float frac = Math.Clamp(item.Durability / archetype.MaxDurability, 0f, 1f);
                    batch.FillRectangle(new RectangleF(sx + 4f, startY + slotSize - 6f, slotSize - 8f, 3f), new ColorRgba(15, 23, 42, 235));
                    batch.FillRectangle(new RectangleF(sx + 4f, startY + slotSize - 6f, (slotSize - 8f) * frac, 3f), new ColorRgba(56, 189, 248, 240));
                }
            }
        }

        string hint = ResolveHintText(viewportWidth, out float hintScale);
        TextMeasureResult measure = font.MeasureString(hint, TextLayoutOptions.Default, hintScale);
        float pillW = measure.Width + 16f;
        float pillX = MathF.Floor((viewportWidth - pillW) * 0.5f);
        batch.FillRectangle(new RectangleF(pillX, viewportHeight - 25f, pillW, 20f), new ColorRgba(10, 15, 28, 210));
    }

    private static void DrawItemIcon(GeometryBatch batch, uint itemId, float cx, float cy)
    {
        switch (itemId)
        {
            case StarterGameCatalog.WoodAxe:
                batch.DrawLine(new Vector2(cx - 10f, cy + 10f), new Vector2(cx + 8f, cy - 8f), new ColorRgba(146, 94, 52, 255), 3.5f);
                batch.FillRectangle(new RectangleF(cx + 2f, cy - 12f, 10f, 8f), new ColorRgba(148, 163, 184, 255));
                break;
            case StarterGameCatalog.StonePickaxe:
                batch.DrawLine(new Vector2(cx - 10f, cy + 10f), new Vector2(cx + 8f, cy - 8f), new ColorRgba(146, 94, 52, 255), 3f);
                batch.DrawLine(new Vector2(cx - 2f, cy - 12f), new Vector2(cx + 12f, cy + 2f), new ColorRgba(156, 163, 175, 255), 4f);
                break;
            case StarterGameCatalog.CrystalPickaxe:
                batch.DrawLine(new Vector2(cx - 10f, cy + 10f), new Vector2(cx + 8f, cy - 8f), new ColorRgba(120, 72, 42, 255), 3f);
                batch.DrawLine(new Vector2(cx - 2f, cy - 12f), new Vector2(cx + 12f, cy + 2f), new ColorRgba(56, 189, 248, 255), 4f);
                break;
            case StarterGameCatalog.IronSword:
                batch.DrawLine(new Vector2(cx - 11f, cy + 11f), new Vector2(cx + 11f, cy - 11f), new ColorRgba(226, 232, 240, 255), 3.5f);
                batch.DrawLine(new Vector2(cx - 8f, cy + 1f), new Vector2(cx - 1f, cy + 8f), new ColorRgba(217, 119, 6, 255), 3.5f);
                break;
            case StarterGameCatalog.WoodLog:
                batch.FillRectangle(new RectangleF(cx - 12f, cy - 5f, 24f, 10f), new ColorRgba(120, 78, 42, 255));
                batch.FillCircle(new Vector2(cx + 12f, cy), 5f, new ColorRgba(214, 175, 130, 255), 10);
                break;
            case StarterGameCatalog.StoneChunk:
                batch.FillCircle(new Vector2(cx, cy + 1f), 10f, new ColorRgba(148, 163, 184, 255), 7);
                batch.FillCircle(new Vector2(cx - 3f, cy - 2f), 4.5f, new ColorRgba(203, 213, 225, 255), 6);
                break;
            case StarterGameCatalog.IronOre:
                batch.FillCircle(new Vector2(cx, cy), 10f, new ColorRgba(100, 116, 139, 255), 7);
                batch.FillCircle(new Vector2(cx + 3f, cy - 2f), 3.8f, new ColorRgba(234, 88, 12, 255), 6);
                batch.FillCircle(new Vector2(cx - 4f, cy + 3f), 3.2f, new ColorRgba(249, 115, 22, 255), 6);
                break;
            case StarterGameCatalog.ManaCrystal:
                batch.FillTriangle(new Vector2(cx, cy - 13f), new Vector2(cx - 9f, cy + 8f), new Vector2(cx + 9f, cy + 8f), new ColorRgba(34, 211, 238, 255));
                break;
            case StarterGameCatalog.BerryHerb:
                batch.FillCircle(new Vector2(cx - 4f, cy + 3f), 5f, new ColorRgba(220, 38, 38, 255), 10);
                batch.FillCircle(new Vector2(cx + 4f, cy + 2f), 5f, new ColorRgba(239, 68, 68, 255), 10);
                batch.FillCircle(new Vector2(cx, cy - 4f), 5f, new ColorRgba(34, 197, 94, 255), 10);
                break;
            case StarterGameCatalog.WoodWallKit:
                batch.FillRectangle(new RectangleF(cx - 10f, cy - 10f, 20f, 20f), new ColorRgba(146, 94, 52, 255));
                batch.DrawRectangle(new RectangleF(cx - 10f, cy - 10f, 20f, 20f), new ColorRgba(217, 179, 130, 255), 1.5f);
                break;
            case StarterGameCatalog.StoneWallKit:
                batch.FillRectangle(new RectangleF(cx - 10f, cy - 10f, 20f, 20f), new ColorRgba(100, 116, 139, 255));
                batch.DrawRectangle(new RectangleF(cx - 10f, cy - 10f, 20f, 20f), new ColorRgba(203, 213, 225, 255), 1.5f);
                break;
            case StarterGameCatalog.WoodDoorKit:
                batch.FillRectangle(new RectangleF(cx - 7f, cy - 11f, 14f, 22f), new ColorRgba(161, 98, 7, 255));
                batch.FillCircle(new Vector2(cx + 3f, cy), 2.2f, new ColorRgba(250, 204, 21, 255), 8);
                break;
            default:
                batch.FillCircle(new Vector2(cx, cy), 7f, new ColorRgba(203, 213, 225, 255), 10);
                break;
        }
    }

    private static void DrawCraftingPanelGeometry(GeometryBatch batch, int viewportWidth, int viewportHeight)
    {
        const float width = 560f;
        const float height = 210f;
        float x = MathF.Floor((viewportWidth - width) * 0.5f);
        float y = viewportHeight - height - 102f;

        RectangleF panel = new(x, y, width, height);
        batch.FillRectangle(panel, new ColorRgba(10, 15, 28, 232));
        batch.DrawRectangle(panel, new ColorRgba(250, 204, 21, 230), 1.5f);
    }

    private static void DrawVitalsText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        IClientWorldSession session,
        int ptx,
        int pty,
        BiomeType biome,
        uint activeItemId,
        float hourOfDay,
        float fps)
    {
        ColorRgba black = new(10, 12, 18, 255);
        int curHp = (int)MathF.Ceiling(session.LocalPlayerHealth);
        int maxHp = (int)MathF.Ceiling(session.LocalPlayerMaxHealth);

        string heroName = string.IsNullOrWhiteSpace(session.ActiveCharacterName)
            ? "CARBON HERO"
            : session.ActiveCharacterName.ToUpperInvariant();
        if (heroName.Length > 15)
        {
            heroName = heroName[..15];
        }

        spriteBatch.DrawOutlinedString(font, heroName, new Vector2(26f, 23f), ColorRgba.White, black, 1, 2f);
        string badgeText = session.TransportLabel;
        TextMeasureResult badgeSize = font.MeasureString(badgeText, TextLayoutOptions.Default, 1.5f);
        float badgeX = MathF.Floor(288f + ((94f - badgeSize.Width) * 0.5f));
        spriteBatch.DrawOutlinedString(font, badgeText, new Vector2(badgeX, 24f), ColorRgba.White, black, 1, 1.5f);

        string hpText = $"HP {curHp}/{maxHp}";
        TextMeasureResult hpSize = font.MeasureString(hpText, TextLayoutOptions.Default, 2f);
        float hpX = MathF.Floor(26f + ((356f - hpSize.Width) * 0.5f));
        spriteBatch.DrawOutlinedString(font, hpText, new Vector2(hpX, 50f), ColorRgba.White, black, 1, 2f);

        int h = (int)MathF.Floor(hourOfDay) % 24;
        int m = (int)MathF.Floor((hourOfDay - MathF.Floor(hourOfDay)) * 60f);
        string itemName = StarterGameCatalog.GetDisplayName(activeItemId);
        spriteBatch.DrawOutlinedString(font, $"{itemName} | {biome}", new Vector2(26f, 76f), new ColorRgba(226, 232, 240, 255), black, 1, 2f);
        spriteBatch.DrawOutlinedString(font, $"({ptx},{pty}) {h:D2}:{m:D2} | {(int)fps} FPS", new Vector2(26f, 100f), new ColorRgba(148, 163, 184, 255), black, 1, 2f);
    }

    private static void DrawMinimapHeaderText(SpriteBatch spriteBatch, BitmapFont font, int viewportWidth, int ptx, int pty)
    {
        float mapX = MathF.Floor(viewportWidth - 176f);
        spriteBatch.DrawOutlinedString(font, $"MAP ({ptx},{pty})", new Vector2(mapX, 20f), new ColorRgba(248, 250, 252, 255), new ColorRgba(10, 12, 18, 255), 1, 2f);
    }

    private static void DrawHotbarText(SpriteBatch spriteBatch, BitmapFont font, IClientWorldSession session, int viewportWidth, int viewportHeight)
    {
        const int slotCount = 9;
        const float slotSize = 48f;
        const float gap = 5f;
        float totalW = (slotCount * slotSize) + ((slotCount - 1) * gap);
        float startX = MathF.Floor((viewportWidth - totalW) * 0.5f);
        float startY = viewportHeight - slotSize - 36f;
        ColorRgba black = new(10, 12, 18, 255);

        for (int i = 0; i < slotCount; i++)
        {
            float sx = startX + (i * (slotSize + gap));
            spriteBatch.DrawOutlinedString(font, $"{i + 1}", new Vector2(sx + 4f, startY + 4f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);
            ItemSlot item = session.Inventory.GetSlot(i);
            if (!item.IsEmpty && item.Quantity > 1)
            {
                string qty = $"x{item.Quantity}";
                TextMeasureResult qSize = font.MeasureString(qty, TextLayoutOptions.Default, 2f);
                spriteBatch.DrawOutlinedString(font, qty, new Vector2(MathF.Floor(sx + slotSize - qSize.Width - 3f), startY + slotSize - 19f), ColorRgba.White, black, 1, 2f);
            }
        }

        string hint = ResolveHintText(viewportWidth, out float hintScale);
        TextMeasureResult hSize = font.MeasureString(hint, TextLayoutOptions.Default, hintScale);
        float hintX = MathF.Floor((viewportWidth - hSize.Width) * 0.5f);
        spriteBatch.DrawOutlinedString(font, hint, new Vector2(hintX, viewportHeight - 22f), new ColorRgba(226, 232, 240, 255), black, 1, hintScale);
    }

    private static void DrawCraftingPanelText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        InventoryContainer inventory,
        int viewportWidth,
        int viewportHeight)
    {
        const float width = 560f;
        const float height = 210f;
        float x = MathF.Floor((viewportWidth - width) * 0.5f);
        float y = viewportHeight - height - 102f;
        ColorRgba black = new(10, 12, 18, 255);

        spriteBatch.DrawOutlinedString(font, "CRAFTING WORKBENCH (1-6)", new Vector2(x + 16f, y + 12f), new ColorRgba(250, 204, 21, 255), black, 1, 2f);

        ReadOnlySpan<CraftingRecipe> recipes = StarterGameCatalog.Recipes.All;
        for (int i = 0; i < recipes.Length; i++)
        {
            CraftingRecipe recipe = recipes[i];
            bool canCraft = RecipeMatcher.CanCraft(inventory, recipe);
            string outName = StarterGameCatalog.GetDisplayName(recipe.Output.ItemId);
            string firstIng = recipe.Ingredients.Count > 0
                ? $"{recipe.Ingredients[0].Quantity} {StarterGameCatalog.GetDisplayName(recipe.Ingredients[0].ItemId)}"
                : string.Empty;
            string secondIng = recipe.Ingredients.Count > 1
                ? $" + {recipe.Ingredients[1].Quantity} {StarterGameCatalog.GetDisplayName(recipe.Ingredients[1].ItemId)}"
                : string.Empty;

            ColorRgba rowColor = canCraft ? new ColorRgba(74, 222, 128, 255) : new ColorRgba(148, 163, 184, 255);
            string tag = canCraft ? "[OK]" : "[--]";
            string line = $"[{recipe.Id}] {outName} x{recipe.Output.Quantity}: {firstIng}{secondIng} {tag}";
            spriteBatch.DrawOutlinedString(font, line, new Vector2(x + 16f, y + 40f + (i * 26f)), rowColor, black, 1, 2f);
        }
    }

    private static string ResolveHintText(int viewportWidth, out float scale)
    {
        if (viewportWidth >= 1000)
        {
            scale = 2f;
            return "[WASD] Move [Space] Hit [F] Mine [1-9] Slot [C] Craft [Esc] Menu";
        }

        scale = 1f;
        return "[WASD] Move [Space/LMB] Hit [F] Harvest [1-9] Slot [C] Craft [Esc] Menu";
    }

    private static ColorRgba ResolveMinimapBiomeColor(BiomeType biome)
    {
        if (biome == BiomeType.DeepOcean)
        {
            return new ColorRgba(17, 45, 92, 255);
        }

        if (biome == BiomeType.ShallowWater)
        {
            return new ColorRgba(38, 109, 168, 255);
        }

        if (biome == BiomeType.BeachSand)
        {
            return new ColorRgba(225, 200, 140, 255);
        }

        if (biome == BiomeType.AncientForest)
        {
            return new ColorRgba(36, 98, 52, 255);
        }

        if (biome == BiomeType.MistySwamp)
        {
            return new ColorRgba(62, 92, 74, 255);
        }

        if (biome == BiomeType.DuneDesert || biome == BiomeType.Savanna)
        {
            return new ColorRgba(209, 174, 96, 255);
        }

        if (biome == BiomeType.RedMesa)
        {
            return new ColorRgba(176, 86, 54, 255);
        }

        if (biome == BiomeType.TropicalJungle)
        {
            return new ColorRgba(28, 124, 62, 255);
        }

        if (biome == BiomeType.FrozenTundra || biome == BiomeType.AlpinePeaks)
        {
            return new ColorRgba(214, 228, 240, 255);
        }

        if (biome == BiomeType.VolcanicAshland)
        {
            return new ColorRgba(78, 66, 68, 255);
        }

        if (biome == BiomeType.BlossomWoods)
        {
            return new ColorRgba(196, 114, 158, 255);
        }

        return new ColorRgba(82, 152, 78, 255);
    }
}
