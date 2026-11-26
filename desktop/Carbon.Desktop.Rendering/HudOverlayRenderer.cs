// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Maps.Data;

namespace Carbon.Desktop.Rendering;

/// <summary>
/// Screen-space MMORPG HUD facade composing specialized single-responsibility RPG widgets
/// framed by standardized 9-slice <see cref="CarbonUiSkin"/> panels and strict 3-pass Z-ordered rendering (SRP / ISP / DIP).
/// </summary>
public sealed class HudOverlayRenderer : IDisposable
{
    private readonly GeometryBatch _geometryBatch;
    private readonly BitmapFont _font;
    private readonly Texture2D _whitePixel;
    private readonly CarbonUiSkin _uiSkin;
    private readonly IVectorArtRepository? _art;
    private readonly HeroVitalsWidget _heroVitals = new();
    private readonly MonsterTargetWidget _targetWidget = new();
    private readonly MinimapRadarWidget _minimap = new();
    private readonly HotbarAndItemWidget _hotbar = new();
    private readonly SkillBarWidget _skillBar = new();
    private readonly CraftingModalWidget _craftModal = new();
    private readonly InventoryModalWidget _bagModal = new();
    private readonly ForgeAnvilModalWidget _forgeModal = new();
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
        _uiSkin = CarbonUiSkin.Load(device);
        _art = artRepository ?? CarbonVectorArtLibrary.LoadOrDefault();
    }

    /// <summary>Gets the embedded bitmap pixel font shared with world-space nameplates &amp; floating combat text.</summary>
    public BitmapFont Font => _font;

    /// <summary>Gets the shared standardized 9-slice UI skin for modal &amp; pause overlays.</summary>
    public CarbonUiSkin UiSkin => _uiSkin;

    /// <summary>Gets or sets whether the centered Crafting Workshop modal (<c>[C]</c>) is open.</summary>
    public bool ShowCraftingPanel { get; set; }

    /// <summary>Gets or sets whether the 40-slot Inventory &amp; Equipment modal (<c>[I]</c>) is open.</summary>
    public bool ShowInventoryModal { get; set; }

    /// <summary>Gets or sets whether the Blacksmith Forge Anvil modal (<c>[U]</c>) is open.</summary>
    public bool ShowForgeModal { get; set; }

    /// <summary>Gets whether any full-screen modal (Bag, Workshop, or Forge Anvil) is currently open.</summary>
    public bool HasActiveModal => this.ShowInventoryModal || this.ShowCraftingPanel || this.ShowForgeModal;

    /// <summary>Gets or sets the currently selected inventory slot (<c>0..39</c>) inside the Bag modal, or <c>-1</c>.</summary>
    public int SelectedInventorySlot
    {
        get => _bagModal.SelectedSlot;
        set => _bagModal.SelectedSlot = value;
    }

    /// <summary>Gets or sets the inventory slot index currently being dragged by the cursor, or <c>-1</c>.</summary>
    public int DraggedInventorySlot
    {
        get => _bagModal.DraggedSlot;
        set => _bagModal.DraggedSlot = value;
    }

    /// <summary>Gets or sets whether the HUD overlay is visible.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Queues a brief fading notification toast on the right edge of the HUD.</summary>
    public void PushNotification(string message) => _toasts.Push(message);

    /// <summary>Scrolls the Crafting Workshop recipe list up or down.</summary>
    public void ScrollCrafting(int delta) => _craftModal.Scroll(delta);

    /// <summary>Hit-tests screen-space pointer coordinates against the 9 hotbar slots.</summary>
    public bool TryHitTestHotbar(Vector2 mousePos, int viewportWidth, int viewportHeight, out byte slotIndex)
    {
        slotIndex = 0;
        return this.Visible && viewportWidth > 0 && viewportHeight > 0 &&
            _hotbar.TryHitTestHotbar(mousePos, viewportWidth, viewportHeight, out slotIndex);
    }

    /// <summary>Hit-tests screen-space pointer coordinates against the quick-access <c>BAG/CRAFT/FORGE</c> dock buttons.</summary>
    public bool TryHitTestHudDockButton(Vector2 mousePos, int viewportWidth, int viewportHeight, out HudDockAction action)
    {
        action = HudDockAction.None;
        return this.Visible && viewportWidth > 0 && viewportHeight > 0 &&
            _hotbar.TryHitTestDockButton(mousePos, viewportWidth, viewportHeight, out action);
    }

    /// <summary>Hit-tests clicks inside the centered Crafting Workshop modal.</summary>
    public bool TryHandleCraftingClick(
        Vector2 mousePos,
        IClientWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        out ushort craftRecipeId,
        out ushort craftQuantity)
    {
        ArgumentNullException.ThrowIfNull(world);
        craftRecipeId = 0;
        craftQuantity = 0;
        return this.Visible && this.ShowCraftingPanel && viewportWidth > 0 && viewportHeight > 0 &&
            _craftModal.TryHandleClick(mousePos, world, viewportWidth, viewportHeight, out craftRecipeId, out craftQuantity);
    }

    /// <summary>Returns whether <paramref name="mousePos"/> hits the <c>[X]</c> close button of whichever modal is open.</summary>
    public bool TryHitTestModalCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        if (!this.Visible || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        if (this.ShowInventoryModal)
        {
            return _bagModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight);
        }

        if (this.ShowCraftingPanel)
        {
            return _craftModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight);
        }

        if (this.ShowForgeModal)
        {
            return _forgeModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight);
        }

        return false;
    }

    /// <summary>Returns whether <paramref name="mousePos"/> falls inside the currently open modal window.</summary>
    public bool IsPointInActiveModal(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        if (!this.Visible || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        if (this.ShowInventoryModal)
        {
            return _bagModal.ContainsPoint(mousePos, viewportWidth, viewportHeight);
        }

        if (this.ShowCraftingPanel)
        {
            return _craftModal.ContainsPoint(mousePos, viewportWidth, viewportHeight);
        }

        if (this.ShowForgeModal)
        {
            return _forgeModal.ContainsPoint(mousePos, viewportWidth, viewportHeight);
        }

        return false;
    }

    /// <summary>Hit-tests screen-space coordinates against the 3 active skill icons.</summary>
    public bool TryHitTestSkill(Vector2 mousePos, int viewportWidth, int viewportHeight, out byte skillSlot)
    {
        skillSlot = 0;
        return this.Visible && viewportWidth > 0 && viewportHeight > 0 &&
            _skillBar.TryHitTestSkill(mousePos, viewportWidth, viewportHeight, out skillSlot);
    }

    /// <summary>Hit-tests screen-space coordinates against any of the 40 slots in the Inventory/Equipment modal.</summary>
    public bool TryHitTestInventorySlot(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort slotIndex)
    {
        slotIndex = 0;
        return this.Visible && this.ShowInventoryModal && viewportWidth > 0 && viewportHeight > 0 &&
            _bagModal.TryHitTestSlot(mousePos, viewportWidth, viewportHeight, out slotIndex);
    }

    /// <summary>Returns whether <paramref name="mousePos"/> hits the modal Equip / Move button inside the Bag.</summary>
    public bool TryHitTestInventoryUseEquipButton(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.Visible && this.ShowInventoryModal && viewportWidth > 0 && viewportHeight > 0 &&
        _bagModal.TryHitTestUseEquipButton(mousePos, viewportWidth, viewportHeight);

    /// <summary>Returns whether <paramref name="mousePos"/> hits the modal Drop-1 item button.</summary>
    public bool TryHitTestInventoryDropButton(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.Visible && this.ShowInventoryModal && viewportWidth > 0 && viewportHeight > 0 &&
        _bagModal.TryHitTestDropButton(mousePos, viewportWidth, viewportHeight);

    /// <summary>Returns whether <paramref name="mousePos"/> hits the modal Drop-All stack button.</summary>
    public bool TryHitTestInventoryDropAllButton(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.Visible && this.ShowInventoryModal && viewportWidth > 0 && viewportHeight > 0 &&
        _bagModal.TryHitTestDropAllButton(mousePos, viewportWidth, viewportHeight);

    /// <summary>Resolves the active item slot targeted by the Bag modal actions.</summary>
    public int ResolveInventoryActionTargetSlot(IClientWorldReadModel world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return _bagModal.ResolveActionTargetSlot(world);
    }

    /// <summary>Hit-tests gear row selection inside the Blacksmith Forge Anvil modal.</summary>
    public bool TrySelectForgeGear(Vector2 mousePos, IClientWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        return this.Visible && this.ShowForgeModal && viewportWidth > 0 && viewportHeight > 0 &&
            _forgeModal.TrySelectGearAt(mousePos, world, viewportWidth, viewportHeight);
    }

    /// <summary>Returns whether <paramref name="mousePos"/> hits the Forge Enhance action button inside the Forge Anvil modal.</summary>
    public bool TryHitTestForgeAnvilButton(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.Visible && this.ShowForgeModal && viewportWidth > 0 && viewportHeight > 0 &&
        _forgeModal.TryHitTestForgeButton(mousePos, viewportWidth, viewportHeight);

    /// <summary>Resolves the selected upgradable gear slot inside the Forge Anvil modal.</summary>
    public int ResolveForgeTargetSlot(IClientWorldReadModel world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return _forgeModal.ResolveTargetSlot(world);
    }

    /// <summary>Renders all screen-space RPG HUD widgets and active modals in strict 3-pass Z order.</summary>
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

        _targetWidget.UpdateTarget(world, feedback, aimedWorldPos);

        Matrix4x4 screenProj = Matrix4x4.CreateOrthographicOffCenter(0f, viewportWidth, viewportHeight, 0f, -1f, 1f);
        Vector2 playerPos = world.LocalPlayerInterpolatedPosition;
        float tileSize = world.ChunkWorld.TileSize;
        int ptx = (int)MathF.Floor(playerPos.X / tileSize);
        int pty = (int)MathF.Floor(playerPos.Y / tileSize);
        MapTile tile = world.ChunkWorld.GetTileAtWorld(ptx, pty);

        Span<ItemSlot> slots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
        world.CopyInventorySlots(slots);
        ItemSlot activeItem = world.SelectedHotbarSlot < slots.Length ? slots[world.SelectedHotbarSlot] : ItemSlot.Empty;
        Vector2? bgMouse = this.HasActiveModal ? null : mouseScreenPos;

        // Pass 1: Background HUD (NineSlice Skins -> Vector Geometry & Bars -> Bitmap Text)
        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
        _heroVitals.DrawSkin(spriteBatch, _uiSkin);
        _targetWidget.DrawSkin(spriteBatch, _uiSkin, viewportWidth, _animTime);
        _minimap.DrawSkin(spriteBatch, _uiSkin, viewportWidth);
        _hotbar.DrawSkin(spriteBatch, _uiSkin, world, viewportWidth, viewportHeight, this.ShowInventoryModal, this.ShowCraftingPanel, this.ShowForgeModal);
        _skillBar.DrawSkin(spriteBatch, _uiSkin, world, viewportWidth, viewportHeight);
        _toasts.DrawSkin(spriteBatch, _uiSkin, _font, viewportWidth);
        spriteBatch.End();

        _geometryBatch.Begin(screenProj);
        _heroVitals.DrawGeometry(_geometryBatch, world, _art, feedback, _animTime);
        _targetWidget.DrawGeometry(_geometryBatch, _art, viewportWidth, _animTime);
        _minimap.DrawGeometry(_geometryBatch, world, viewportWidth, ptx, pty);
        int hoveredSlot = _hotbar.DrawGeometry(_geometryBatch, _font, world, _art, viewportWidth, viewportHeight, activeItem.ItemId, bgMouse);
        int hoveredSkill = _skillBar.DrawGeometry(_geometryBatch, world, viewportWidth, viewportHeight, bgMouse);
        _geometryBatch.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
        _heroVitals.DrawText(spriteBatch, _font, world, ptx, pty, tile.Biome, activeItem.ItemId, hourOfDay, _smoothedFps);
        _targetWidget.DrawText(spriteBatch, _font, _art, viewportWidth);
        _minimap.DrawText(spriteBatch, _font, viewportWidth, ptx, pty);
        _hotbar.DrawText(spriteBatch, _font, world, _art, viewportWidth, viewportHeight, activeItem.ItemId, hoveredSlot);
        _skillBar.DrawText(spriteBatch, _font, world, viewportWidth, viewportHeight, hoveredSkill);
        _toasts.DrawText(spriteBatch, _font, viewportWidth);
        spriteBatch.End();

        // Pass 2 & 3: Active Modal Window & Floating Tooltip/Drag Layer
        if (this.ShowInventoryModal)
        {
            _geometryBatch.Begin(screenProj);
            _geometryBatch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 8, 16, 138));
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _bagModal.DrawSkin(spriteBatch, _uiSkin, world, viewportWidth, viewportHeight, mouseScreenPos);
            spriteBatch.End();

            _geometryBatch.Begin(screenProj);
            int hoveredModalSlot = _bagModal.DrawGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight, mouseScreenPos, _animTime);
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _bagModal.DrawText(spriteBatch, _font, world, viewportWidth, viewportHeight);
            spriteBatch.End();

            _geometryBatch.Begin(screenProj);
            _bagModal.DrawTopOverlayGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight, mouseScreenPos, hoveredModalSlot);
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _bagModal.DrawTopOverlayText(spriteBatch, _font, world, viewportWidth, viewportHeight, mouseScreenPos, hoveredModalSlot);
            spriteBatch.End();
        }
        else if (this.ShowCraftingPanel)
        {
            _geometryBatch.Begin(screenProj);
            _geometryBatch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 8, 16, 138));
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _craftModal.DrawSkin(spriteBatch, _uiSkin, world, viewportWidth, viewportHeight, mouseScreenPos);
            spriteBatch.End();

            _geometryBatch.Begin(screenProj);
            _craftModal.DrawGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight);
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _craftModal.DrawText(spriteBatch, _font, world, viewportWidth, viewportHeight);
            spriteBatch.End();
        }
        else if (this.ShowForgeModal)
        {
            _geometryBatch.Begin(screenProj);
            _geometryBatch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 8, 16, 138));
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _forgeModal.DrawSkin(spriteBatch, _uiSkin, world, viewportWidth, viewportHeight, mouseScreenPos);
            spriteBatch.End();

            _geometryBatch.Begin(screenProj);
            _forgeModal.DrawGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight);
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _forgeModal.DrawText(spriteBatch, _font, world, viewportWidth, viewportHeight);
            spriteBatch.End();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _uiSkin.Dispose();
            _geometryBatch.Dispose();
            _font.Dispose();
            _whitePixel.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
