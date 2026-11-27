// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Desktop.Rendering.Feedback;
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
using Carbon.Desktop.Rendering.Fonts;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Screen-space MMORPG HUD facade composing single-responsibility RPG widgets
/// framed by <see cref="CarbonUiSkin"/> + <see cref="CarbonUiPalette"/> in strict 3-pass Z order (SRP / ISP / DIP).
/// </summary>
public sealed class HudOverlayRenderer : IDisposable, IInventoryInputHud
{
    private readonly GeometryBatch _geometryBatch;
    private readonly IGraphicsDevice _device;
    private BitmapFont _font;
    private int _fontLanguageVersion;
    private readonly Texture2D _whitePixel;
    private readonly CarbonUiSkin _uiSkin;
    private readonly IVectorArtRepository? _art;
    private readonly MonsterTargetWidget _targetWidget = new();
    private readonly MinimapRadarWidget _minimap;
    private readonly DayNightClockWidget _clock = new();
    private readonly HotbarAndItemWidget _hotbar = new();
    private readonly DeathScreenWidget _deathScreen = new();
    private readonly SkillBarWidget _skillBar = new();
    private readonly InventoryModalWidget _bagModal = new();
    private readonly CharacterSheetModalWidget _characterModal = new();
    private readonly CraftingModalWidget _craftModal = new();
    private readonly ClassSkillsModalWidget _skillsModal = new();
    private readonly ForgeAnvilModalWidget _forgeModal = new();
    private readonly SettingsModalWidget _settingsModal = new();
    private readonly ToastFeedWidget _toasts = new();
    private readonly List<SkillDefinition> _skillScratch = new(8);

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
        _device = device;
        _geometryBatch = new GeometryBatch(device);
        _fontLanguageVersion = GameText.Current.Version;
        (_font, _whitePixel) = GameFontLoader.Load(device);
        _uiSkin = CarbonUiSkin.Load(device);
        _art = artRepository ?? CarbonVectorArtLibrary.LoadOrDefault();
        _minimap = new MinimapRadarWidget(device);
    }

    /// <summary>Gets the persisted client HUD &amp; display settings.</summary>
    public UiSettings Settings { get; } = UiSettings.LoadOrDefault();

    /// <summary>Gets the primary bitmap font shared with world-space nameplates &amp; floating combat text.</summary>
    public BitmapFont Font => _font;

    /// <summary>Gets the shared standardized 9-slice UI skin.</summary>
    public CarbonUiSkin UiSkin => _uiSkin;

    /// <summary>Gets or sets whether the unified tabbed player window (Inventory/Character/Crafting/Skills) is open.</summary>
    public bool ShowPlayerModal { get; set; }

    /// <summary>Gets or sets the active tab of the unified player modal window.</summary>
    public PlayerModalTab ActivePlayerTab { get; set; } = PlayerModalTab.Inventory;

    /// <summary>Gets or sets whether the Blacksmith Forge Anvil modal is open.</summary>
    public bool ShowForgeModal { get; set; }

    /// <summary>Gets or sets the open trader window's stock (ADR-025), or <see langword="null"/> when it is closed.</summary>
    public TraderView? Trader { get; set; }

    /// <summary>Gets or sets whether the blacksmith's trade window is in repair mode (bag clicks repair instead of sell).</summary>
    public bool TradeRepairMode { get; set; }

    /// <summary>
    /// Gets or sets a death shown instead of the session's (screenshot preview only, <c>CARBON_UI_PREVIEW=death</c>).
    /// Visual only: nothing is sent to the server.
    /// </summary>
    public DeathView? DeathPreview { get; set; }

    /// <summary>Returns the death screen's Respawn button rectangle (ADR-031).</summary>
    public static RectangleF GetRespawnButtonRect(int viewportWidth, int viewportHeight) =>
        DeathScreenWidget.GetRespawnButtonRect(viewportWidth, viewportHeight);

    /// <summary>Gets or sets whether the System Settings &amp; Controls modal is open.</summary>
    public bool ShowSettingsModal { get; set; }

    /// <summary>Gets whether any full-screen modal (Player Window, Forge, or Settings) is open.</summary>
    public bool HasActiveModal =>
        this.ShowPlayerModal || this.ShowForgeModal || this.ShowSettingsModal || this.Trader is not null;

    /// <summary>Gets or sets the inventory slot index currently held/carried by the cursor, or <c>-1</c>.</summary>
    public int CarriedInventorySlot
    {
        get => _bagModal.CarriedSlot;
        set => _bagModal.CarriedSlot = value;
    }

    /// <summary>Gets the inventory split/confirm dialog state (ADR-020).</summary>
    public InventoryDialogState InventoryDialog => _bagModal.Dialog;

    /// <summary>Gets or sets whether the HUD overlay is visible.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Opens the Settings modal on the Display (<c>0</c>) or Controls (<c>1</c>) tab.</summary>
    /// <param name="tab">Tab index.</param>
    public void OpenSettings(int tab)
    {
        this.CloseAllModals();
        _settingsModal.SelectTab(tab);
        this.ShowSettingsModal = true;
    }

    /// <summary>Marks a terrain chunk for a minimap re-bake (forwarded from the session's dirty-chunk feed).</summary>
    public void InvalidateMinimapChunk(int chunkX, int chunkY) => _minimap.InvalidateChunk(chunkX, chunkY);

    /// <summary>Zooms the minimap when the cursor is over it; returns whether the wheel was consumed.</summary>
    /// <param name="mousePos">Cursor position.</param>
    /// <param name="steps">Positive to zoom in.</param>
    /// <param name="viewportWidth">Viewport width.</param>
    public bool TryZoomMinimap(Vector2 mousePos, int steps, int viewportWidth) =>
        this.Visible && this.Settings.ShowMinimap && !this.HasActiveModal && _minimap.TryZoom(mousePos, steps, viewportWidth);

    /// <summary>Queues a brief fading notification toast on the right edge of the HUD.</summary>
    public void PushNotification(string message) => _toasts.Push(message);

    /// <summary>Returns whether <paramref name="world"/> currently has at least one accessible class skill.</summary>
    public bool HasAccessibleClassSkills(IWorldReadModel world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return PlayerModalTabs.HasClassSkills(world, _skillScratch);
    }

    /// <summary>
    /// Opens or switches to <paramref name="tab"/>, or closes the player modal if already viewing <paramref name="tab"/>.
    /// Returns <see langword="false"/> if <paramref name="tab"/> is <see cref="PlayerModalTab.Skills"/> and the character has no class skills.
    /// </summary>
    public bool OpenOrTogglePlayerTab(IWorldReadModel world, PlayerModalTab tab)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (tab == PlayerModalTab.Skills && !this.HasAccessibleClassSkills(world))
        {
            return false;
        }

        if (this.ShowPlayerModal && this.ActivePlayerTab == tab)
        {
            this.ShowPlayerModal = false;
            _bagModal.CarriedSlot = -1;
            return true;
        }

        this.ShowForgeModal = false;
        this.ShowSettingsModal = false;
        this.ShowPlayerModal = true;
        this.ActivePlayerTab = tab;
        if (tab != PlayerModalTab.Inventory)
        {
            _bagModal.CarriedSlot = -1;
        }

        return true;
    }

    /// <summary>Closes all active modals cleanly.</summary>
    public void CloseAllModals(bool keepSettings = false)
    {
        this.ShowPlayerModal = false;
        this.ShowForgeModal = false;
        this.Trader = null;
        if (!keepSettings)
        {
            this.ShowSettingsModal = false;
        }

        _bagModal.CarriedSlot = -1;
    }

    /// <summary>Scrolls the Crafting Workshop recipe list up or down.</summary>
    public void ScrollCrafting(int delta) => _craftModal.Scroll(delta);

    /// <summary>Gets the forge hammer animation (outcome VFX and SFX cues, ADR-020).</summary>
    public ForgeEnhancementVfx ForgeVfx => _forgeModal.Vfx;

    /// <summary>Returns the protection scroll picked in the forge window for the selected item (<c>0</c> for none).</summary>
    public ushort ResolveForgeProtection(IWorldReadModel world) => _forgeModal.ResolveProtection(world);

    /// <summary>Scrolls the Blacksmith Forge Anvil gear list up or down.</summary>
    public void ScrollForge(int delta) => _forgeModal.Scroll(delta);

    /// <summary>Hit-tests screen-space pointer coordinates against the 9 hotbar slots.</summary>
    public bool TryHitTestHotbar(Vector2 mousePos, int viewportWidth, int viewportHeight, out byte slotIndex)
    {
        slotIndex = 0;
        return this.Visible && viewportWidth > 0 && viewportHeight > 0 &&
            _hotbar.TryHitTestHotbar(mousePos, viewportWidth, viewportHeight, out slotIndex);
    }

    /// <summary>Hit-tests top-left window tab clicks when the unified player window is open.</summary>
    public bool TryHitTestPlayerModalTab(
        IWorldReadModel world,
        Vector2 mousePos,
        int viewportWidth,
        int viewportHeight,
        out PlayerModalTab tab)
    {
        ArgumentNullException.ThrowIfNull(world);
        tab = PlayerModalTab.Inventory;
        if (!this.Visible || !this.ShowPlayerModal || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        bool hit = this.ActivePlayerTab switch
        {
            PlayerModalTab.Inventory => _bagModal.TryHitTestTab(world, mousePos, viewportWidth, viewportHeight, out tab),
            PlayerModalTab.Character => _characterModal.TryHitTestTab(world, mousePos, viewportWidth, viewportHeight, out tab),
            PlayerModalTab.Crafting => _craftModal.TryHitTestTab(world, mousePos, viewportWidth, viewportHeight, out tab),
            PlayerModalTab.Skills => _skillsModal.TryHitTestTab(world, mousePos, viewportWidth, viewportHeight, out tab),
            _ => false,
        };

        if (hit && tab == PlayerModalTab.Skills && !this.HasAccessibleClassSkills(world))
        {
            return false;
        }

        return hit;
    }

    /// <summary>Hit-tests clicks inside the Crafting tab of the player modal.</summary>
    public bool TryHandleCraftingClick(
        Vector2 mousePos,
        IWorldReadModel world,
        int viewportWidth,
        int viewportHeight,
        out ushort craftRecipeId,
        out ushort craftQuantity)
    {
        ArgumentNullException.ThrowIfNull(world);
        craftRecipeId = 0;
        craftQuantity = 0;
        return this.Visible && this.ShowPlayerModal && this.ActivePlayerTab == PlayerModalTab.Crafting && viewportWidth > 0 && viewportHeight > 0 &&
            _craftModal.TryHandleClick(mousePos, world, viewportWidth, viewportHeight, out craftRecipeId, out craftQuantity);
    }

    /// <summary>Hit-tests clicks inside the System Settings &amp; Controls modal.</summary>
    public SettingsModalAction HandleSettingsClick(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.Visible && this.ShowSettingsModal && viewportWidth > 0 && viewportHeight > 0
            ? _settingsModal.HandleClick(mousePos, this.Settings, viewportWidth, viewportHeight)
            : SettingsModalAction.None;

    /// <summary>Hit-tests unlock/rank-up buttons inside the Class Skills tab.</summary>
    public bool TryHitTestSkillUpgrade(
        IWorldReadModel world,
        Vector2 mousePos,
        int viewportWidth,
        int viewportHeight,
        out ushort skillId)
    {
        ArgumentNullException.ThrowIfNull(world);
        skillId = 0;
        return this.Visible && this.ShowPlayerModal && this.ActivePlayerTab == PlayerModalTab.Skills && viewportWidth > 0 && viewportHeight > 0 &&
            _skillsModal.TryHitTestUpgradeButton(world, mousePos, viewportWidth, viewportHeight, out skillId);
    }

    /// <summary>Hit-tests <c>+</c> attribute allocation buttons inside the Character Info tab (<c>[Y]</c>).</summary>
    public bool TryHitTestCharacterStatPlus(Vector2 mousePos, int viewportWidth, int viewportHeight, out PrimaryAttributeId attribute)
    {
        attribute = PrimaryAttributeId.Vitality;
        return this.Visible && this.ShowPlayerModal && this.ActivePlayerTab == PlayerModalTab.Character && viewportWidth > 0 && viewportHeight > 0 &&
            _characterModal.TryHitTestStatPlusButton(mousePos, viewportWidth, viewportHeight, out attribute);
    }

    /// <summary>
    /// Handles a click on the Character tab's attribute reset button (ADR-021): the first click arms it, the second
    /// confirms. Returns <see langword="true"/> only for the confirming click.
    /// </summary>
    public bool TryClickCharacterReset(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.Visible && this.ShowPlayerModal && this.ActivePlayerTab == PlayerModalTab.Character && viewportWidth > 0 && viewportHeight > 0 &&
        _characterModal.TryClickResetButton(world, mousePos, viewportWidth, viewportHeight);

    /// <summary>Returns whether <paramref name="mousePos"/> hits the close button of whichever modal is open.</summary>
    public bool TryHitTestModalCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        if (!this.Visible || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        if (this.ShowPlayerModal)
        {
            return this.ActivePlayerTab switch
            {
                PlayerModalTab.Inventory => _bagModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Character => _characterModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Crafting => _craftModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Skills => _skillsModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight),
                _ => false,
            };
        }

        if (this.ShowForgeModal)
        {
            return _forgeModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight);
        }

        if (this.Trader is not null)
        {
            return TradeModalWidget.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight);
        }

        return false;
    }

    /// <summary>Returns whether <paramref name="mousePos"/> falls inside the currently open modal window (including top tabs).</summary>
    public bool IsPointInActiveModal(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!this.Visible || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        if (this.ShowSettingsModal)
        {
            return _settingsModal.ContainsPoint(mousePos, viewportWidth, viewportHeight);
        }

        if (this.ShowPlayerModal)
        {
            return this.ActivePlayerTab switch
            {
                PlayerModalTab.Inventory => _bagModal.ContainsPoint(world, mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Character => _characterModal.ContainsPoint(world, mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Crafting => _craftModal.ContainsPoint(world, mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Skills => _skillsModal.ContainsPoint(world, mousePos, viewportWidth, viewportHeight),
                _ => false,
            };
        }

        if (this.ShowForgeModal)
        {
            return _forgeModal.ContainsPoint(mousePos, viewportWidth, viewportHeight);
        }

        if (this.Trader is not null)
        {
            return TradeModalWidget.ContainsPoint(mousePos, viewportWidth, viewportHeight);
        }

        return false;
    }

    /// <summary>Hit-tests screen-space coordinates against the right-edge class skill bar.</summary>
    public bool TryHitTestSkill(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort skillId)
    {
        ArgumentNullException.ThrowIfNull(world);
        skillId = 0;
        return this.Visible && viewportWidth > 0 && viewportHeight > 0 &&
            this.PrepareSkillBar(viewportWidth) &&
            _skillBar.TryHitTestSkill(world, mousePos, viewportWidth, viewportHeight, out skillId);
    }

    /// <summary>Gets the screen-space centre of inventory slot <paramref name="slotIndex"/> (screenshot previews).</summary>
    public static Vector2 GetInventorySlotCenter(int slotIndex, int viewportWidth, int viewportHeight)
    {
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        RectangleF rect = InventoryModalWidget.GetSlotRectForTesting(in layout, slotIndex);
        return new Vector2(rect.X + (rect.Width * 0.5f), rect.Y + (rect.Height * 0.5f));
    }

    /// <summary>Hit-tests the inventory dialog's buttons.</summary>
    public InventoryDialogButton HitTestInventoryDialog(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        _bagModal.HitTestDialog(mousePos, viewportWidth, viewportHeight);

    /// <summary>Hit-tests the inventory tab's Sort button.</summary>
    public bool TryHitTestInventorySortButton(Vector2 mousePos, int viewportWidth, int viewportHeight) =>
        this.Visible && this.ShowPlayerModal && this.ActivePlayerTab == PlayerModalTab.Inventory &&
        _bagModal.TryHitTestSortButton(mousePos, viewportWidth, viewportHeight);

    /// <summary>Hit-tests screen-space coordinates against any of the 42 slots in the Inventory tab.</summary>
    public bool TryHitTestInventorySlot(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort slotIndex)
    {
        slotIndex = 0;
        return this.Visible && this.ShowPlayerModal && this.ActivePlayerTab == PlayerModalTab.Inventory && viewportWidth > 0 && viewportHeight > 0 &&
            _bagModal.TryHitTestSlot(mousePos, viewportWidth, viewportHeight, out slotIndex);
    }

    /// <summary>Hit-tests gear row selection inside the Blacksmith Forge Anvil modal.</summary>
    public bool TrySelectForgeGear(Vector2 mousePos, IWorldReadModel world, int viewportWidth, int viewportHeight)
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
    public int ResolveForgeTargetSlot(IWorldReadModel world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return _forgeModal.ResolveTargetSlot(world);
    }

    /// <summary>Renders all screen-space RPG HUD widgets and active modals in strict 3-pass Z order.</summary>
    public void Render(
        IWorldReadModel world,
        SpriteBatch spriteBatch,
        int viewportWidth,
        int viewportHeight,
        float frameDt,
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

        _ = this.PrepareSkillBar(viewportWidth);
        _animTime += frameDt;
        _forgeModal.Update(frameDt, world);
        _toasts.Step(frameDt);
        if (frameDt > 0.0001f)
        {
            float instFps = Math.Clamp(1f / frameDt, 1f, 999f);
            _smoothedFps += (instFps - _smoothedFps) * 0.08f;
        }

        this.ReloadFontIfLanguageChanged();
        _targetWidget.UpdateTarget(world, feedback, aimedWorldPos);

        Matrix4x4 screenProj = Matrix4x4.CreateOrthographicOffCenter(0f, viewportWidth, viewportHeight, 0f, -1f, 1f);
        Vector2 playerPos = world.LocalPlayerInterpolatedPosition;
        float tileSize = world.ChunkWorld.TileSize;
        int ptx = (int)MathF.Floor(playerPos.X / tileSize);
        int pty = (int)MathF.Floor(playerPos.Y / tileSize);
        Vector2? bgMouse = this.HasActiveModal ? null : mouseScreenPos;

        // Pass 1: Background HUD (NineSlice Skins -> Vector Geometry & Bars -> Bitmap Text), walked from HudComposition.MainHud.
        HudFrame frame = new(world, viewportWidth, viewportHeight, ptx, pty, bgMouse, feedback);
        this.DrawElements(HudComposition.MainHud, spriteBatch, screenProj, in frame);

        // Pass 2 & 3: Active Modal Window & Floating Tooltip/Drag Layer
        if (this.ShowSettingsModal)
        {
            _geometryBatch.Begin(screenProj);
            _geometryBatch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 6, 12, 150));
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _settingsModal.DrawSkin(spriteBatch, _uiSkin, this.Settings, viewportWidth, viewportHeight, mouseScreenPos);
            _settingsModal.DrawText(spriteBatch, _font, this.Settings, viewportWidth, viewportHeight);
            spriteBatch.End();
        }
        else if (this.ShowPlayerModal)
        {
            _geometryBatch.Begin(screenProj);
            _geometryBatch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 6, 12, 138));
            _geometryBatch.End();

            switch (this.ActivePlayerTab)
            {
                case PlayerModalTab.Inventory:
                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _bagModal.DrawSkin(spriteBatch, _uiSkin, world, viewportWidth, viewportHeight, mouseScreenPos);
                    spriteBatch.End();

                    _geometryBatch.Begin(screenProj);
                    int hoveredModalSlot = _bagModal.DrawGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight, mouseScreenPos, _animTime);
                    _geometryBatch.End();

                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _bagModal.DrawText(spriteBatch, _font, world, viewportWidth, viewportHeight);
                    spriteBatch.End();

                    this.DrawElements(HudComposition.InventoryHeader, spriteBatch, screenProj, in frame);

                    _geometryBatch.Begin(screenProj);
                    _bagModal.DrawTopOverlayGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight, mouseScreenPos, hoveredModalSlot);
                    _geometryBatch.End();

                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _bagModal.DrawTopOverlayText(spriteBatch, _font, world, viewportWidth, viewportHeight, mouseScreenPos, hoveredModalSlot);
                    spriteBatch.End();
                    break;

                case PlayerModalTab.Character:
                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _characterModal.DrawSkin(spriteBatch, _uiSkin, world, viewportWidth, viewportHeight, mouseScreenPos);
                    spriteBatch.End();

                    _geometryBatch.Begin(screenProj);
                    _characterModal.DrawGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight, _animTime);
                    _geometryBatch.End();

                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _characterModal.DrawText(spriteBatch, _font, world, viewportWidth, viewportHeight, ptx, pty, _smoothedFps, this.Settings.ShowTelemetry);
                    spriteBatch.End();
                    break;

                case PlayerModalTab.Crafting:
                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _craftModal.DrawSkin(spriteBatch, _uiSkin, world, viewportWidth, viewportHeight, mouseScreenPos);
                    spriteBatch.End();

                    _geometryBatch.Begin(screenProj);
                    _craftModal.DrawGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight);
                    _geometryBatch.End();

                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _craftModal.DrawText(spriteBatch, _font, world, viewportWidth, viewportHeight);
                    spriteBatch.End();
                    break;

                case PlayerModalTab.Skills:
                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _skillsModal.DrawSkin(spriteBatch, _uiSkin, world, viewportWidth, viewportHeight, mouseScreenPos);
                    spriteBatch.End();

                    _geometryBatch.Begin(screenProj);
                    _skillsModal.DrawGeometry(_geometryBatch, world, viewportWidth, viewportHeight);
                    _geometryBatch.End();

                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _skillsModal.DrawText(spriteBatch, _font, world, viewportWidth, viewportHeight);
                    spriteBatch.End();
                    break;

                default:
                    break;
            }
        }
        else if (this.Trader is TraderView trader)
        {
            TradeModalWidget.Render(spriteBatch, _geometryBatch, _uiSkin, _font, _art, screenProj, world, trader, this.TradeRepairMode, viewportWidth, viewportHeight, mouseScreenPos);
        }
        else if (this.ShowForgeModal)
        {
            _geometryBatch.Begin(screenProj);
            _geometryBatch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 6, 12, 138));
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

        // The death screen covers everything else while the player is dead (ADR-031).
        if ((this.DeathPreview ?? world.LocalDeath) is DeathView death)
        {
            bool pending = this.DeathPreview is null && world.IsRespawnPending;
            _geometryBatch.Begin(screenProj);
            DeathScreenWidget.DrawVeil(_geometryBatch, viewportWidth, viewportHeight);
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            DeathScreenWidget.DrawSkin(spriteBatch, _uiSkin, pending, viewportWidth, viewportHeight, mouseScreenPos);
            spriteBatch.End();

            _geometryBatch.Begin(screenProj);
            DeathScreenWidget.DrawGeometry(_geometryBatch, _art, in death, viewportWidth, viewportHeight);
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _deathScreen.DrawText(spriteBatch, _font, in death, pending, viewportWidth, viewportHeight);
            spriteBatch.End();
        }
    }

    /// <summary>
    /// Draws <paramref name="elements"/> in three passes (skins, geometry, text). Every list goes through this one
    /// dispatcher, so whatever a <see cref="HudComposition"/> list names is exactly what reaches the screen.
    /// </summary>
    private void DrawElements(ReadOnlySpan<HudElement> elements, SpriteBatch spriteBatch, Matrix4x4 screenProj, in HudFrame frame)
    {
        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
        for (int i = 0; i < elements.Length; i++)
        {
            this.DrawElementSkin(elements[i], spriteBatch, in frame);
        }

        spriteBatch.End();

        int hoveredSlot = -1;
        ushort hoveredSkill = 0;
        _geometryBatch.Begin(screenProj);
        for (int i = 0; i < elements.Length; i++)
        {
            this.DrawElementGeometry(elements[i], in frame, ref hoveredSlot, ref hoveredSkill);
        }

        _geometryBatch.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
        for (int i = 0; i < elements.Length; i++)
        {
            this.DrawElementText(elements[i], spriteBatch, in frame, hoveredSlot, hoveredSkill);
        }

        spriteBatch.End();
    }

    private void DrawElementSkin(HudElement element, SpriteBatch spriteBatch, in HudFrame f)
    {
        switch (element)
        {
            case HudElement.TargetFrame:
                _targetWidget.DrawSkin(spriteBatch, _uiSkin, f.ViewportWidth);
                break;
            case HudElement.Clock:
                _clock.DrawSkin(spriteBatch, _uiSkin, f.ViewportWidth, f.World.AuthoritativeTimeHour, f.World.AuthoritativeIsNight);
                break;
            case HudElement.Minimap:
                if (this.Settings.ShowMinimap)
                {
                    _minimap.DrawSkin(spriteBatch, _uiSkin, f.World, _art, f.ViewportWidth);
                }

                break;
            case HudElement.Hotbar:
                _hotbar.DrawSkin(spriteBatch, _uiSkin, f.World, f.ViewportWidth, f.ViewportHeight);
                break;
            case HudElement.SkillBar:
                _skillBar.DrawSkin(spriteBatch, _uiSkin, f.World, f.ViewportWidth, f.ViewportHeight);
                break;
            case HudElement.Toasts:
                _toasts.DrawSkin(spriteBatch, _uiSkin, _font, f.ViewportWidth, HudLayout.GetToastTop(f.ViewportWidth, this.Settings.ShowMinimap));
                break;
            case HudElement.Wallet:
            default:
                break;
        }
    }

    private void DrawElementGeometry(HudElement element, in HudFrame f, ref int hoveredSlot, ref ushort hoveredSkill)
    {
        switch (element)
        {
            case HudElement.TargetFrame:
                _targetWidget.DrawGeometry(_geometryBatch, _art, f.ViewportWidth, _animTime);
                break;
            case HudElement.Clock:
                _clock.DrawGeometry(_geometryBatch, f.ViewportWidth, f.World.AuthoritativeTimeHour, f.World.AuthoritativeIsNight);
                break;
            case HudElement.Minimap:
                if (this.Settings.ShowMinimap)
                {
                    _minimap.DrawGeometry(_geometryBatch, f.World, _art, f.ViewportWidth);
                }

                break;
            case HudElement.Hotbar:
                hoveredSlot = _hotbar.DrawGeometry(_geometryBatch, f.World, _art, f.Feedback, f.ViewportWidth, f.ViewportHeight, f.BackgroundMouse, _animTime);
                break;
            case HudElement.SkillBar:
                hoveredSkill = _skillBar.DrawGeometry(_geometryBatch, f.World, f.ViewportWidth, f.ViewportHeight, f.BackgroundMouse);
                break;
            case HudElement.Wallet:
                PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(f.ViewportWidth, f.ViewportHeight);
                WalletWidget.DrawGeometry(_geometryBatch, _art, in layout);
                break;
            case HudElement.Toasts:
            default:
                break;
        }
    }

    private void DrawElementText(HudElement element, SpriteBatch spriteBatch, in HudFrame f, int hoveredSlot, ushort hoveredSkill)
    {
        switch (element)
        {
            case HudElement.TargetFrame:
                _targetWidget.DrawText(spriteBatch, _font, _art, f.ViewportWidth);
                break;
            case HudElement.Clock:
                _clock.DrawText(spriteBatch, _font, f.ViewportWidth, f.World.AuthoritativeTimeHour, f.World.AuthoritativeIsNight);
                break;
            case HudElement.Minimap:
                if (this.Settings.ShowMinimap)
                {
                    _minimap.DrawText(spriteBatch, _font, f.ViewportWidth, f.Ptx, f.Pty, f.BackgroundMouse);
                }

                break;
            case HudElement.Hotbar:
                _hotbar.DrawText(spriteBatch, _font, f.World, _art, f.ViewportWidth, f.ViewportHeight, hoveredSlot);
                break;
            case HudElement.SkillBar:
                _skillBar.DrawText(spriteBatch, _font, f.World, f.ViewportWidth, f.ViewportHeight, hoveredSkill);
                break;
            case HudElement.Toasts:
                _toasts.DrawText(spriteBatch, _font, f.ViewportWidth, HudLayout.GetToastTop(f.ViewportWidth, this.Settings.ShowMinimap));
                break;
            case HudElement.Wallet:
                PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(f.ViewportWidth, f.ViewportHeight);
                WalletWidget.DrawText(spriteBatch, _font, f.World, in layout);
                break;
            default:
                break;
        }
    }

    /// <summary>Per-frame inputs shared by every element of a draw list.</summary>
    private readonly record struct HudFrame(
        IWorldReadModel World,
        int ViewportWidth,
        int ViewportHeight,
        int Ptx,
        int Pty,
        Vector2? BackgroundMouse,
        CombatFeedbackTracker? Feedback);

    /// <summary>Keeps the skill bar under the clock/minimap column (<see cref="HudLayout"/>); always returns <see langword="true"/>.</summary>
    private bool PrepareSkillBar(int viewportWidth)
    {
        _skillBar.MinTop = HudLayout.GetSkillBarMinTop(viewportWidth, this.Settings.ShowMinimap);
        return true;
    }

    /// <inheritdoc/>
    private void ReloadFontIfLanguageChanged()
    {
        int version = GameText.Current.Version;
        if (version == _fontLanguageVersion)
        {
            return;
        }

        _fontLanguageVersion = version;
        BitmapFont previous = _font;
        _font = GameFontLoader.LoadFont(_device);
        previous.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _uiSkin.Dispose();
            _minimap.Dispose();
            _geometryBatch.Dispose();
            _font.Dispose();
            _whitePixel.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
