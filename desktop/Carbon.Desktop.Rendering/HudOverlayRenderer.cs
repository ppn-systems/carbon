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
/// biome/clock telemetry, and toggleable crafting menu.
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
        _healthBar = new ProgressBar(new RectangleF(24f, 36f, 210f, 14f), 100f, 100f)
        {
            BackgroundTexture = _whitePixel,
            BackgroundSource = new RectangleF(0f, 0f, 1f, 1f),
            FillTexture = _whitePixel,
            FillSource = new RectangleF(0f, 0f, 1f, 1f),
            FillColor = new ColorRgba(220, 38, 38, 255),
            GhostColor = new ColorRgba(250, 204, 21, 230),
            BackgroundColor = new ColorRgba(15, 23, 42, 230),
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
        DrawHotbarGeometry(_geometryBatch, session, viewportWidth, viewportHeight);
        if (this.ShowCraftingPanel)
        {
            DrawCraftingPanelGeometry(_geometryBatch, viewportWidth, viewportHeight);
        }

        _geometryBatch.End();

        // 2. ProgressBar + crisp outlined pixel text
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
        RectangleF card = new(14f, 14f, 252f, 82f);
        batch.FillRectangle(card, new ColorRgba(10, 15, 28, 200));
        batch.DrawRectangle(card, new ColorRgba(71, 85, 105, 220), 1.5f);

        ColorRgba badgeColor = session.IsOnlineMultiplayer
            ? new ColorRgba(16, 185, 129, 220)
            : new ColorRgba(59, 130, 246, 220);
        batch.FillRectangle(new RectangleF(204f, 20f, 54f, 12f), badgeColor);
    }

    private static void DrawMinimap(GeometryBatch batch, IClientWorldSession session, int viewportWidth, int ptx, int pty)
    {
        const float mapSize = 128f;
        const float pad = 4f;
        float mapX = viewportWidth - mapSize - 18f;
        float mapY = 30f;

        RectangleF outer = new(mapX - pad, mapY - pad - 14f, mapSize + (pad * 2f), mapSize + (pad * 2f) + 14f);
        batch.FillRectangle(outer, new ColorRgba(10, 15, 28, 210));
        batch.DrawRectangle(outer, new ColorRgba(148, 163, 184, 220), 1.5f);

        const int cells = 32;
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
            batch.FillCircle(new Vector2(ax, ay), 2.5f, dotColor, 8);
        }

        batch.FillCircle(new Vector2(centerPxX, centerPxY), 3.4f, new ColorRgba(255, 255, 255, 255), 10);
        batch.DrawCircle(new Vector2(centerPxX, centerPxY), 4.2f, new ColorRgba(15, 23, 42, 255), 10, 1f);
    }

    private static void DrawHotbarGeometry(GeometryBatch batch, IClientWorldSession session, int viewportWidth, int viewportHeight)
    {
        const int slotCount = 9;
        const float slotSize = 44f;
        const float gap = 4f;
        float totalW = (slotCount * slotSize) + ((slotCount - 1) * gap);
        float startX = (viewportWidth - totalW) * 0.5f;
        float startY = viewportHeight - slotSize - 26f;

        batch.FillRectangle(new RectangleF(startX - 6f, startY - 6f, totalW + 12f, slotSize + 12f), new ColorRgba(10, 15, 28, 210));
        InventoryContainer inv = session.Inventory;

        for (int i = 0; i < slotCount; i++)
        {
            float sx = startX + (i * (slotSize + gap));
            RectangleF rect = new(sx, startY, slotSize, slotSize);
            bool active = i == session.SelectedHotbarSlot;

            batch.FillRectangle(rect, active ? new ColorRgba(30, 41, 59, 240) : new ColorRgba(15, 23, 42, 220));
            batch.DrawRectangle(
                rect,
                active ? new ColorRgba(250, 204, 21, 255) : new ColorRgba(71, 85, 105, 220),
                active ? 2.2f : 1f);

            ItemSlot item = inv.GetSlot(i);
            if (!item.IsEmpty)
            {
                DrawItemIcon(batch, item.ItemId, sx + (slotSize * 0.5f), startY + (slotSize * 0.5f));
                if (item.Durability > 0f && StarterGameCatalog.Items.TryGet(item.ItemId, out ItemArchetype? archetype) && archetype.MaxDurability > 0f)
                {
                    float frac = Math.Clamp(item.Durability / archetype.MaxDurability, 0f, 1f);
                    batch.FillRectangle(new RectangleF(sx + 4f, startY + slotSize - 5f, slotSize - 8f, 3f), new ColorRgba(15, 23, 42, 230));
                    batch.FillRectangle(new RectangleF(sx + 4f, startY + slotSize - 5f, (slotSize - 8f) * frac, 3f), new ColorRgba(56, 189, 248, 240));
                }
            }
        }
    }

    private static void DrawItemIcon(GeometryBatch batch, uint itemId, float cx, float cy)
    {
        switch (itemId)
        {
            case StarterGameCatalog.WoodAxe:
                batch.DrawLine(new Vector2(cx - 9f, cy + 9f), new Vector2(cx + 7f, cy - 7f), new ColorRgba(146, 94, 52, 255), 3f);
                batch.FillRectangle(new RectangleF(cx + 2f, cy - 11f, 9f, 7f), new ColorRgba(148, 163, 184, 255));
                break;
            case StarterGameCatalog.StonePickaxe:
                batch.DrawLine(new Vector2(cx - 9f, cy + 9f), new Vector2(cx + 7f, cy - 7f), new ColorRgba(146, 94, 52, 255), 2.5f);
                batch.DrawLine(new Vector2(cx - 2f, cy - 11f), new Vector2(cx + 11f, cy + 2f), new ColorRgba(156, 163, 175, 255), 3.5f);
                break;
            case StarterGameCatalog.CrystalPickaxe:
                batch.DrawLine(new Vector2(cx - 9f, cy + 9f), new Vector2(cx + 7f, cy - 7f), new ColorRgba(120, 72, 42, 255), 2.5f);
                batch.DrawLine(new Vector2(cx - 2f, cy - 11f), new Vector2(cx + 11f, cy + 2f), new ColorRgba(56, 189, 248, 255), 3.5f);
                break;
            case StarterGameCatalog.IronSword:
                batch.DrawLine(new Vector2(cx - 10f, cy + 10f), new Vector2(cx + 10f, cy - 10f), new ColorRgba(226, 232, 240, 255), 3f);
                batch.DrawLine(new Vector2(cx - 7f, cy + 1f), new Vector2(cx - 1f, cy + 7f), new ColorRgba(217, 119, 6, 255), 3f);
                break;
            case StarterGameCatalog.WoodLog:
                batch.FillRectangle(new RectangleF(cx - 11f, cy - 5f, 22f, 10f), new ColorRgba(120, 78, 42, 255));
                batch.FillCircle(new Vector2(cx + 11f, cy), 5f, new ColorRgba(214, 175, 130, 255), 10);
                break;
            case StarterGameCatalog.StoneChunk:
                batch.FillCircle(new Vector2(cx, cy + 1f), 9f, new ColorRgba(148, 163, 184, 255), 7);
                batch.FillCircle(new Vector2(cx - 3f, cy - 2f), 4f, new ColorRgba(203, 213, 225, 255), 6);
                break;
            case StarterGameCatalog.IronOre:
                batch.FillCircle(new Vector2(cx, cy), 9f, new ColorRgba(100, 116, 139, 255), 7);
                batch.FillCircle(new Vector2(cx + 3f, cy - 2f), 3.5f, new ColorRgba(234, 88, 12, 255), 6);
                batch.FillCircle(new Vector2(cx - 4f, cy + 3f), 3f, new ColorRgba(249, 115, 22, 255), 6);
                break;
            case StarterGameCatalog.ManaCrystal:
                batch.FillTriangle(new Vector2(cx, cy - 12f), new Vector2(cx - 8f, cy + 7f), new Vector2(cx + 8f, cy + 7f), new ColorRgba(34, 211, 238, 255));
                break;
            case StarterGameCatalog.BerryHerb:
                batch.FillCircle(new Vector2(cx - 4f, cy + 3f), 4.5f, new ColorRgba(220, 38, 38, 255), 10);
                batch.FillCircle(new Vector2(cx + 4f, cy + 2f), 4.5f, new ColorRgba(239, 68, 68, 255), 10);
                batch.FillCircle(new Vector2(cx, cy - 3f), 4.5f, new ColorRgba(34, 197, 94, 255), 10);
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
                batch.FillCircle(new Vector2(cx + 3f, cy), 2f, new ColorRgba(250, 204, 21, 255), 8);
                break;
            default:
                batch.FillCircle(new Vector2(cx, cy), 7f, new ColorRgba(203, 213, 225, 255), 10);
                break;
        }
    }

    private static void DrawCraftingPanelGeometry(GeometryBatch batch, int viewportWidth, int viewportHeight)
    {
        const float width = 340f;
        const float height = 166f;
        float x = (viewportWidth - width) * 0.5f;
        float y = viewportHeight - height - 86f;

        RectangleF panel = new(x, y, width, height);
        batch.FillRectangle(panel, new ColorRgba(10, 15, 28, 225));
        batch.DrawRectangle(panel, new ColorRgba(250, 204, 21, 220), 1.5f);
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

        spriteBatch.DrawOutlinedString(font, "CARBON HERO", new Vector2(24f, 20f), ColorRgba.White, black, 1, 1.2f);
        spriteBatch.DrawOutlinedString(font, session.IsOnlineMultiplayer ? "ONLINE" : "OFFLINE", new Vector2(209f, 22f), ColorRgba.White, black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"HP {curHp}/{maxHp}", new Vector2(90f, 39f), ColorRgba.White, black, 1, 1.1f);

        int h = (int)MathF.Floor(hourOfDay) % 24;
        int m = (int)MathF.Floor((hourOfDay - MathF.Floor(hourOfDay)) * 60f);
        string itemName = StarterGameCatalog.GetDisplayName(activeItemId);
        spriteBatch.DrawOutlinedString(font, $"Item: {itemName} | {biome}", new Vector2(24f, 58f), new ColorRgba(203, 213, 225, 255), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"Tile ({ptx},{pty}) | {h:D2}:{m:D2} | {(int)fps} FPS", new Vector2(24f, 74f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
    }

    private static void DrawMinimapHeaderText(SpriteBatch spriteBatch, BitmapFont font, int viewportWidth, int ptx, int pty)
    {
        float mapX = viewportWidth - 146f;
        spriteBatch.DrawOutlinedString(font, $"RADAR ({ptx},{pty})", new Vector2(mapX, 16f), new ColorRgba(248, 250, 252, 255), new ColorRgba(10, 12, 18, 255), 1, 1f);
    }

    private static void DrawHotbarText(SpriteBatch spriteBatch, BitmapFont font, IClientWorldSession session, int viewportWidth, int viewportHeight)
    {
        const int slotCount = 9;
        const float slotSize = 44f;
        const float gap = 4f;
        float totalW = (slotCount * slotSize) + ((slotCount - 1) * gap);
        float startX = (viewportWidth - totalW) * 0.5f;
        float startY = viewportHeight - slotSize - 26f;
        ColorRgba black = new(10, 12, 18, 255);

        for (int i = 0; i < slotCount; i++)
        {
            float sx = startX + (i * (slotSize + gap));
            spriteBatch.DrawOutlinedString(font, $"{i + 1}", new Vector2(sx + 4f, startY + 4f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
            ItemSlot item = session.Inventory.GetSlot(i);
            if (!item.IsEmpty && item.Quantity > 1)
            {
                spriteBatch.DrawOutlinedString(font, $"x{item.Quantity}", new Vector2(sx + 18f, startY + slotSize - 15f), ColorRgba.White, black, 1, 1f);
            }
        }

        string hint = "[WASD] Move [Space/LMB] Attack [F] Harvest [1-9] Slot [C] Craft [Tab] HUD";
        spriteBatch.DrawOutlinedString(font, hint, new Vector2(startX - 8f, viewportHeight - 16f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
    }

    private static void DrawCraftingPanelText(
        SpriteBatch spriteBatch,
        BitmapFont font,
        InventoryContainer inventory,
        int viewportWidth,
        int viewportHeight)
    {
        const float width = 340f;
        const float height = 166f;
        float x = (viewportWidth - width) * 0.5f;
        float y = viewportHeight - height - 86f;
        ColorRgba black = new(10, 12, 18, 255);

        spriteBatch.DrawOutlinedString(font, "CRAFTING WORKBENCH (Press 1-6)", new Vector2(x + 14f, y + 10f), new ColorRgba(250, 204, 21, 255), black, 1, 1.1f);

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
            spriteBatch.DrawOutlinedString(font, line, new Vector2(x + 14f, y + 32f + (i * 20f)), rowColor, black, 1, 1f);
        }
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
