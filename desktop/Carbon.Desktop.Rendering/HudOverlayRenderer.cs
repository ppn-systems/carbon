// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Items.Containers;
using Carbon.Maps.Data;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Screen-space MMORPG HUD facade composing specialized single-responsibility RPG widgets
/// (<see cref="HeroVitalsWidget"/>, <see cref="MonsterTargetWidget"/>, <see cref="MinimapRadarWidget"/>,
/// <see cref="HotbarAndItemWidget"/>, <see cref="CraftingWorkbenchWidget"/>, and <see cref="ToastFeedWidget"/>).
/// Depends strictly on read-only <see cref="IClientWorldReadModel"/> and <see cref="IVectorArtRepository"/> (ISP / DIP).
/// </summary>
public sealed class HudOverlayRenderer : IDisposable
{
    private readonly GeometryBatch _geometryBatch;
    private readonly BitmapFont _font;
    private readonly Texture2D _whitePixel;
    private readonly IVectorArtRepository? _art;
    private readonly HeroVitalsWidget _heroVitals = new();
    private readonly MonsterTargetWidget _targetWidget = new();
    private readonly MinimapRadarWidget _minimap = new();
    private readonly HotbarAndItemWidget _hotbar = new();
    private readonly SkillBarWidget _skillBar = new();
    private readonly CraftingWorkbenchWidget _workbench = new();
    private readonly InventoryForgeModalWidget _inventoryModal = new();
    private readonly ToastFeedWidget _toasts = new();

    private float _smoothedFps = 60f;
    private float _animTime;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HudOverlayRenderer"/> class.
    /// </summary>
    /// <param name="device">Active OpenGL graphics device.</param>
    /// <param name="artRepository">Optional injected declarative vector-art repository (DIP).</param>
    public HudOverlayRenderer(IGraphicsDevice device, IVectorArtRepository? artRepository = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _geometryBatch = new GeometryBatch(device);
        (_font, _whitePixel) = EmbeddedPixelFont.Create(device);
        _art = artRepository ?? CarbonVectorArtLibrary.LoadOrDefault();
    }

    /// <summary>Gets the embedded bitmap pixel font shared with world-space nameplates &amp; floating combat text.</summary>
    public BitmapFont Font => _font;

    /// <summary>Gets or sets whether the crafting workbench panel is expanded.</summary>
    public bool ShowCraftingPanel { get; set; }

    /// <summary>Gets or sets whether the 40-slot inventory/armor/forge modal is open.</summary>
    public bool ShowInventoryModal { get; set; }

    /// <summary>Gets or sets the currently selected inventory slot (<c>0..39</c>) inside the modal, or <c>-1</c>.</summary>
    public int SelectedInventorySlot
    {
        get => _inventoryModal.SelectedSlot;
        set => _inventoryModal.SelectedSlot = value;
    }

    /// <summary>Gets or sets whether the HUD overlay is visible.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Queues a brief fading notification toast on the right edge of the HUD.</summary>
    public void PushNotification(string message) => _toasts.Push(message);

    /// <summary>Hit-tests screen-space pointer coordinates against the 9 hotbar slots.</summary>
    public bool TryHitTestHotbar(Vector2 mousePos, int viewportWidth, int viewportHeight, out byte slotIndex)
    {
        slotIndex = 0;
        return this.Visible && viewportWidth > 0 && viewportHeight > 0 &&
            _hotbar.TryHitTestHotbar(mousePos, viewportWidth, viewportHeight, out slotIndex);
    }

    /// <summary>Hit-tests screen-space pointer coordinates against rows inside the expanded crafting workbench panel.</summary>
    public bool TryHitTestCraftingRecipe(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort recipeId)
    {
        recipeId = 0;
        return this.Visible && this.ShowCraftingPanel && viewportWidth > 0 && viewportHeight > 0 &&
            _workbench.TryHitTestRecipe(mousePos, viewportWidth, viewportHeight, out recipeId);
    }

    /// <summary>Returns whether <paramref name="mousePos"/> lies inside the expanded crafting workbench panel bounds.</summary>
    public bool IsPointInCraftingPanel(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.Visible && this.ShowCraftingPanel && viewportWidth > 0 && viewportHeight > 0 &&
        _workbench.ContainsPoint(mousePos, viewportWidth, viewportHeight);

    /// <summary>Hit-tests screen-space coordinates against the 3 active skill icons.</summary>
    public bool TryHitTestSkill(Vector2 mousePos, int viewportWidth, int viewportHeight, out byte skillSlot)
    {
        skillSlot = 0;
        return this.Visible && viewportWidth > 0 && viewportHeight > 0 &&
            _skillBar.TryHitTestSkill(mousePos, viewportWidth, viewportHeight, out skillSlot);
    }

    /// <summary>Hit-tests screen-space coordinates against any of the 40 slots in the Inventory/Armor modal.</summary>
    public bool TryHitTestInventorySlot(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort slotIndex)
    {
        slotIndex = 0;
        return this.Visible && this.ShowInventoryModal && viewportWidth > 0 && viewportHeight > 0 &&
            _inventoryModal.TryHitTestSlot(mousePos, viewportWidth, viewportHeight, out slotIndex);
    }

    /// <summary>Returns whether <paramref name="mousePos"/> hits the modal Forge enhancement button.</summary>
    public bool TryHitTestInventoryForgeButton(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.Visible && this.ShowInventoryModal && viewportWidth > 0 && viewportHeight > 0 &&
        _inventoryModal.TryHitTestForgeButton(mousePos, viewportWidth, viewportHeight);

    /// <summary>Returns whether <paramref name="mousePos"/> hits the modal Drop item button.</summary>
    public bool TryHitTestInventoryDropButton(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.Visible && this.ShowInventoryModal && viewportWidth > 0 && viewportHeight > 0 &&
        _inventoryModal.TryHitTestDropButton(mousePos, viewportWidth, viewportHeight);

    /// <summary>Resolves the active item slot targeted by the modal Forge/Drop controls.</summary>
    public int ResolveInventoryActionTargetSlot(IClientWorldReadModel world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return _inventoryModal.ResolveActionTargetSlot(world);
    }

    /// <summary>Renders all screen-space RPG HUD widgets for the current frame.</summary>
    public void Render(
        IClientWorldReadModel world,
        SpriteBatch spriteBatch,
        int viewportWidth,
        int viewportHeight,
        float frameDt,
        float hourOfDay,
        Vector2? mouseScreenPos = null,
        CombatFeedbackTracker? feedback = null,
        Vector2? aimedWorldPos = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(spriteBatch);
        if (!this.Visible || !world.IsReady || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        _animTime += frameDt;
        _toasts.Step(frameDt);
        if (frameDt > 0.0001f)
        {
            float instFps = Math.Clamp(1f / frameDt, 1f, 999f);
            _smoothedFps += (instFps - _smoothedFps) * 0.08f;
        }

        CombatFeedbackTracker resolvedFeedback = feedback ?? new CombatFeedbackTracker();
        _targetWidget.UpdateTarget(world, resolvedFeedback, aimedWorldPos);

        Matrix4x4 screenProj = Matrix4x4.CreateOrthographicOffCenter(0f, viewportWidth, viewportHeight, 0f, -1f, 1f);
        Vector2 playerPos = world.LocalPlayerInterpolatedPosition;
        float tileSize = world.ChunkWorld.TileSize;
        int ptx = (int)MathF.Floor(playerPos.X / tileSize);
        int pty = (int)MathF.Floor(playerPos.Y / tileSize);
        MapTile tile = world.ChunkWorld.GetTileAtWorld(ptx, pty);
        ItemSlot activeItem = world.Inventory.GetSlot(world.SelectedHotbarSlot);

        // Pass 1: Vector portraits, unit frames, minimap tiles, hotbar slots, item icons, and tooltips
        _geometryBatch.Begin(screenProj);
        _heroVitals.DrawGeometry(_geometryBatch, world, _art, resolvedFeedback, _animTime);
        _targetWidget.DrawGeometry(_geometryBatch, _art, viewportWidth, _animTime);
        _minimap.DrawGeometry(_geometryBatch, world, viewportWidth, ptx, pty);
        int hoveredSlot = _hotbar.DrawGeometry(_geometryBatch, _font, world, _art, viewportWidth, viewportHeight, activeItem.ItemId, this.ShowInventoryModal ? null : mouseScreenPos);
        _skillBar.DrawGeometry(_geometryBatch, world, viewportWidth, viewportHeight, mouseScreenPos);
        if (this.ShowCraftingPanel)
        {
            _workbench.DrawGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight, mouseScreenPos);
        }

        int hoveredModalSlot = -1;
        if (this.ShowInventoryModal)
        {
            hoveredModalSlot = _inventoryModal.DrawGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight, mouseScreenPos, _animTime);
        }

        _toasts.DrawGeometry(_geometryBatch, _font, viewportWidth, viewportHeight);
        _geometryBatch.End();

        // Pass 2: Outlined pixel-snapped text
        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
        _heroVitals.DrawText(spriteBatch, _font, world, ptx, pty, tile.Biome, activeItem.ItemId, hourOfDay, _smoothedFps);
        _targetWidget.DrawText(spriteBatch, _font, _art, viewportWidth);
        _minimap.DrawText(spriteBatch, _font, viewportWidth, ptx, pty);
        _hotbar.DrawText(spriteBatch, _font, world, _art, viewportWidth, viewportHeight, activeItem.ItemId, hoveredSlot);
        _skillBar.DrawText(spriteBatch, _font, world, viewportWidth, viewportHeight);
        if (this.ShowCraftingPanel)
        {
            _workbench.DrawText(spriteBatch, _font, world.Inventory, viewportWidth, viewportHeight);
        }

        if (this.ShowInventoryModal)
        {
            _inventoryModal.DrawText(spriteBatch, _font, world, viewportWidth, viewportHeight, hoveredModalSlot);
        }

        _toasts.DrawText(spriteBatch, _font, viewportWidth, viewportHeight);
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
}
