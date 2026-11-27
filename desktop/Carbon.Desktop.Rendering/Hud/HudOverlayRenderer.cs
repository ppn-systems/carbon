// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Rendering.Effects;
using Carbon.Desktop.Rendering.Feedback;
using Carbon.Desktop.Rendering.UiSkin;
using Carbon.Desktop.Rendering.Water;
using Carbon.Desktop.Rendering.VectorArt;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.OpenGL.Abstractions;
using Carbon.Graphics.OpenGL.Textures;
using Carbon.Desktop.Rendering.Fonts;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>
/// Screen-space MMORPG HUD facade composing single-responsibility RPG widgets
/// framed by <see cref="CarbonUiSkin"/> + <see cref="CarbonUiPalette"/> in strict 3-pass Z order (SRP / ISP / DIP).
/// </summary>
public sealed class HudOverlayRenderer : IDisposable, IInventoryInputHud
{
    private readonly OxygenBarWidget _oxygenBar = new();
    private readonly IGraphicsDevice _device;
    private readonly UiScaleState _ui;
    private readonly bool _ownsUi;

    // GPU resources, created on first use (EnsureGpuResources) so the HUD's UI state can be built without a GL context.
    private GeometryBatch _geometryBatch = null!;
    private BitmapFont _font = null!;
    private int _fontLanguageVersion;
    private Texture2D _whitePixel = null!;
    private CarbonUiSkin _uiSkin = null!;
    private bool _gpuReady;
    private readonly IVectorArtRepository? _art;
    private readonly MonsterTargetWidget _targetWidget = new();
    private readonly MinimapRadarWidget _minimap;
    private readonly DayNightClockWidget _clock = new();
    private readonly HotbarAndItemWidget _hotbar = new();
    private readonly DeathScreenWidget _deathScreen = new();
    private readonly SkillBarWidget _skillBar = new();
    private readonly HudUiState _state;
    private readonly InventoryModalWidget _bagModal;
    private readonly CharacterSheetModalWidget _characterModal = new();
    private readonly CraftingModalWidget _craftModal = new();
    private readonly RepairModalWidget _repairWidget = new();
    private readonly ClassSkillsModalWidget _skillsModal = new();
    private readonly ForgeAnvilModalWidget _forgeModal = new();
    private readonly SettingsModalWidget _settingsModal = new();
    private readonly ToastFeedWidget _toasts = new();
    private readonly StatusEffectStripWidget _statusStrip = new();
    private readonly DamageVignetteWidget _vignette = new();

    private float _smoothedFps = 60f;
    private float _animTime;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="HudOverlayRenderer"/> class. GPU resources (geometry batch, fonts, UI
    /// skin) are created on first use, from the thread that renders, so construction needs no GL context.
    /// </summary>
    /// <param name="device">OpenGL graphics device the HUD renders with.</param>
    /// <param name="artRepository">Optional injected declarative vector-art repository (DIP).</param>
    /// <param name="state">Window state the scene owns and drives; a new one when <see langword="null"/>.</param>
    /// <param name="uiScale">The UI scale shared with the menus; a private one (from <see cref="Settings"/>) when <see langword="null"/>.</param>
    public HudOverlayRenderer(IGraphicsDevice device, IVectorArtRepository? artRepository = null, HudUiState? state = null, UiScaleState? uiScale = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _ownsUi = uiScale is null;
        _ui = uiScale ?? new UiScaleState(() => this.Settings.UiScaleChoice);
        _ui.Choice = this.Settings.UiScaleChoice;
        _state = state ?? new HudUiState();
        _bagModal = new InventoryModalWidget(_state);
        _art = artRepository ?? CarbonVectorArtLibrary.LoadOrDefault();
        _minimap = new MinimapRadarWidget(device);
    }

    /// <summary>Gets the persisted client HUD &amp; display settings.</summary>
    public UiSettings Settings { get; } = UiSettings.LoadOrDefault();

    /// <summary>
    /// Gets the 1x font for world-space nameplates and floating combat text: they follow the camera zoom, not the UI
    /// scale. The HUD's own panels draw with the shared UI-scale font (<see cref="UiScale"/>).
    /// </summary>
    public BitmapFont Font
    {
        get
        {
            this.EnsureGpuResources();
            return _font;
        }
    }

    /// <summary>Gets the shared standardized 9-slice UI skin.</summary>
    public CarbonUiSkin UiSkin
    {
        get
        {
            this.EnsureGpuResources();
            return _uiSkin;
        }
    }

    /// <summary>Returns the death screen's Respawn button rectangle (ADR-031), in window pixels.</summary>
    public RectangleF GetRespawnButtonRect(int viewportWidth, int viewportHeight)
    {
        (_, viewportWidth, viewportHeight) = this.ToCanvas(Vector2.Zero, viewportWidth, viewportHeight);
        RectangleF canvas = DeathScreenWidget.GetRespawnButtonRect(viewportWidth, viewportHeight);
        float scale = _ui.Scale;
        return new RectangleF(canvas.X * scale, canvas.Y * scale, canvas.Width * scale, canvas.Height * scale);
    }

    /// <summary>
    /// Gets the UI scale the HUD shares with the menus (docs/art/menu-ui.md rule 21). Every method here takes the mouse and
    /// the viewport in window pixels and maps them onto the scale's canvas itself.
    /// </summary>
    public UiScaleState UiScale => _ui;

    /// <summary>Gets the window state this HUD draws (owned and driven by the scene, audit OOP-3).</summary>
    public HudUiState State => _state;

    /// <inheritdoc/>
    bool IInventoryInputHud.ShowPlayerModal
    {
        get => _state.ShowPlayerModal;
        set => _state.ShowPlayerModal = value;
    }

    /// <inheritdoc/>
    int IInventoryInputHud.CarriedInventorySlot
    {
        get => _state.CarriedInventorySlot;
        set => _state.CarriedInventorySlot = value;
    }

    /// <summary>Gets the inventory split/confirm dialog state (ADR-020).</summary>
    public InventoryDialogState InventoryDialog => _bagModal.Dialog;

    /// <summary>Opens the Settings modal on the Display (<c>0</c>) or Controls (<c>1</c>) tab.</summary>
    /// <param name="tab">Tab index.</param>
    public void OpenSettings(int tab)
    {
        _state.OpenSettings();
        _settingsModal.SelectTab(tab);
    }

    /// <summary>Marks a terrain chunk for a minimap re-bake (forwarded from the session's dirty-chunk feed).</summary>
    public void InvalidateMinimapChunk(int chunkX, int chunkY) => _minimap.InvalidateChunk(chunkX, chunkY);

    /// <summary>Zooms the minimap when the cursor is over it; returns whether the wheel was consumed.</summary>
    /// <param name="mousePos">Cursor position.</param>
    /// <param name="steps">Positive to zoom in.</param>
    /// <param name="viewportWidth">Viewport width.</param>
    public bool TryZoomMinimap(Vector2 mousePos, int steps, int viewportWidth)
    {
        (mousePos, viewportWidth) = this.ToCanvas(mousePos, viewportWidth);
        return _state.Visible && this.Settings.ShowMinimap && !_state.HasActiveModal && _minimap.TryZoom(mousePos, steps, viewportWidth);
    }

    /// <summary>Queues a brief fading notification toast on the right edge of the HUD.</summary>
    public void PushNotification(string message) => _toasts.Push(message);

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
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        slotIndex = 0;
        return _state.Visible && viewportWidth > 0 && viewportHeight > 0 &&
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
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        tab = PlayerModalTab.Inventory;
        if (!_state.Visible || !_state.ShowPlayerModal || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        bool hit = _state.ActivePlayerTab switch
        {
            PlayerModalTab.Inventory => _bagModal.TryHitTestTab(world, mousePos, viewportWidth, viewportHeight, out tab),
            PlayerModalTab.Character => _characterModal.TryHitTestTab(world, mousePos, viewportWidth, viewportHeight, out tab),
            PlayerModalTab.Crafting => _craftModal.TryHitTestTab(world, mousePos, viewportWidth, viewportHeight, out tab),
            PlayerModalTab.Skills => _skillsModal.TryHitTestTab(world, mousePos, viewportWidth, viewportHeight, out tab),
            _ => false,
        };

        if (hit && tab == PlayerModalTab.Skills && !_state.HasAccessibleClassSkills(world))
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
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        craftRecipeId = 0;
        craftQuantity = 0;
        return _state.Visible && _state.ShowPlayerModal && _state.ActivePlayerTab == PlayerModalTab.Crafting && viewportWidth > 0 && viewportHeight > 0 &&
            _craftModal.TryHandleClick(mousePos, world, viewportWidth, viewportHeight, out craftRecipeId, out craftQuantity);
    }

    /// <summary>Hit-tests clicks inside the System Settings &amp; Controls modal.</summary>
    public SettingsModalAction HandleSettingsClick(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        SettingsModalAction action = _state.Visible && _state.ShowSettingsModal && viewportWidth > 0 && viewportHeight > 0
            ? _settingsModal.HandleClick(mousePos, this.Settings, viewportWidth, viewportHeight)
            : SettingsModalAction.None;
        _ui.Choice = this.Settings.UiScaleChoice; // a new UI scale applies at once, in the world and back in the menus
        return action;
    }

    /// <summary>Hit-tests unlock/rank-up buttons inside the Class Skills tab.</summary>
    public bool TryHitTestSkillUpgrade(
        IWorldReadModel world,
        Vector2 mousePos,
        int viewportWidth,
        int viewportHeight,
        out ushort skillId)
    {
        ArgumentNullException.ThrowIfNull(world);
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        skillId = 0;
        return _state.Visible && _state.ShowPlayerModal && _state.ActivePlayerTab == PlayerModalTab.Skills && viewportWidth > 0 && viewportHeight > 0 &&
            _skillsModal.TryHitTestUpgradeButton(world, mousePos, viewportWidth, viewportHeight, out skillId);
    }

    /// <summary>Hit-tests <c>+</c> attribute allocation buttons inside the Character Info tab (<c>[Y]</c>).</summary>
    public bool TryHitTestCharacterStatPlus(Vector2 mousePos, int viewportWidth, int viewportHeight, out PrimaryAttributeId attribute)
    {
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        attribute = PrimaryAttributeId.Vitality;
        return _state.Visible && _state.ShowPlayerModal && _state.ActivePlayerTab == PlayerModalTab.Character && viewportWidth > 0 && viewportHeight > 0 &&
            _characterModal.TryHitTestStatPlusButton(mousePos, viewportWidth, viewportHeight, out attribute);
    }

    /// <summary>Returns whether <paramref name="mousePos"/> hits the close button of whichever modal is open.</summary>
    public bool TryHitTestModalCloseButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        if (!_state.Visible || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        if (_state.ShowPlayerModal)
        {
            return _state.ActivePlayerTab switch
            {
                PlayerModalTab.Inventory => _bagModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Character => _characterModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Crafting => _craftModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Skills => _skillsModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight),
                _ => false,
            };
        }

        if (_state.ShowForgeModal)
        {
            return _forgeModal.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight);
        }

        if (_state.Trader is not null)
        {
            return TradeModalWidget.TryHitTestCloseButton(mousePos, viewportWidth, viewportHeight);
        }

        if (_state.Repair.IsOpen)
        {
            return RepairLayout.CloseRect(RepairLayout.Origin(viewportWidth, viewportHeight).Ox, RepairLayout.Origin(viewportWidth, viewportHeight).Oy).Contains(mousePos);
        }

        return false;
    }

    /// <summary>Returns whether <paramref name="mousePos"/> falls inside the currently open modal window (including top tabs).</summary>
    public bool IsPointInActiveModal(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        if (!_state.Visible || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return false;
        }

        if (_state.ShowSettingsModal)
        {
            return _settingsModal.ContainsPoint(mousePos, viewportWidth, viewportHeight);
        }

        if (_state.ShowPlayerModal)
        {
            return _state.ActivePlayerTab switch
            {
                PlayerModalTab.Inventory => _bagModal.ContainsPoint(world, mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Character => _characterModal.ContainsPoint(world, mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Crafting => _craftModal.ContainsPoint(world, mousePos, viewportWidth, viewportHeight),
                PlayerModalTab.Skills => _skillsModal.ContainsPoint(world, mousePos, viewportWidth, viewportHeight),
                _ => false,
            };
        }

        if (_state.ShowForgeModal)
        {
            return _forgeModal.ContainsPoint(mousePos, viewportWidth, viewportHeight);
        }

        if (_state.Trader is not null)
        {
            return TradeModalWidget.ContainsPoint(mousePos, viewportWidth, viewportHeight);
        }

        if (_state.Repair.IsOpen)
        {
            return RepairModalWidget.ContainsPoint(mousePos, viewportWidth, viewportHeight);
        }

        return false;
    }

    /// <summary>Hit-tests screen-space coordinates against the right-edge class skill bar.</summary>
    public bool TryHitTestSkill(IWorldReadModel world, Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort skillId)
    {
        ArgumentNullException.ThrowIfNull(world);
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        skillId = 0;
        return _state.Visible && viewportWidth > 0 && viewportHeight > 0 &&
            this.PrepareSkillBar(viewportWidth) &&
            _skillBar.TryHitTestSkill(world, mousePos, viewportWidth, viewportHeight, out skillId);
    }

    /// <summary>Gets the window-pixel centre of inventory slot <paramref name="slotIndex"/> (screenshot previews).</summary>
    public Vector2 GetInventorySlotCenter(int slotIndex, int viewportWidth, int viewportHeight)
    {
        (_, viewportWidth, viewportHeight) = this.ToCanvas(Vector2.Zero, viewportWidth, viewportHeight);
        PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(viewportWidth, viewportHeight);
        RectangleF rect = InventoryModalWidget.GetSlotRectForTesting(in layout, slotIndex);
        return _ui.ToWindow(new Vector2(rect.X + (rect.Width * 0.5f), rect.Y + (rect.Height * 0.5f)));
    }

    /// <summary>Hit-tests the inventory dialog's buttons.</summary>
    public InventoryDialogButton HitTestInventoryDialog(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        return _bagModal.HitTestDialog(mousePos, viewportWidth, viewportHeight);
    }

    /// <summary>Hit-tests the inventory tab's Sort button.</summary>
    public bool TryHitTestInventorySortButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        return _state.Visible && _state.ShowPlayerModal && _state.ActivePlayerTab == PlayerModalTab.Inventory &&
            _bagModal.TryHitTestSortButton(mousePos, viewportWidth, viewportHeight);
    }

    /// <summary>Hit-tests screen-space coordinates against any of the 42 slots in the Inventory tab.</summary>
    public bool TryHitTestInventorySlot(Vector2 mousePos, int viewportWidth, int viewportHeight, out ushort slotIndex)
    {
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        slotIndex = 0;
        return _state.Visible && _state.ShowPlayerModal && _state.ActivePlayerTab == PlayerModalTab.Inventory && viewportWidth > 0 && viewportHeight > 0 &&
            _bagModal.TryHitTestSlot(mousePos, viewportWidth, viewportHeight, out slotIndex);
    }

    /// <summary>Hit-tests gear row selection inside the Blacksmith Forge Anvil modal.</summary>
    public bool TrySelectForgeGear(Vector2 mousePos, IWorldReadModel world, int viewportWidth, int viewportHeight)
    {
        ArgumentNullException.ThrowIfNull(world);
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        return _state.Visible && _state.ShowForgeModal && viewportWidth > 0 && viewportHeight > 0 &&
            _forgeModal.TrySelectGearAt(mousePos, world, viewportWidth, viewportHeight);
    }

    /// <summary>Returns whether <paramref name="mousePos"/> hits the Forge Enhance action button inside the Forge Anvil modal.</summary>
    public bool TryHitTestForgeAnvilButton(Vector2 mousePos, int viewportWidth, int viewportHeight)
    {
        (mousePos, viewportWidth, viewportHeight) = this.ToCanvas(mousePos, viewportWidth, viewportHeight);
        return _state.Visible && _state.ShowForgeModal && viewportWidth > 0 && viewportHeight > 0 &&
            _forgeModal.TryHitTestForgeButton(mousePos, viewportWidth, viewportHeight);
    }

    /// <summary>Resolves the selected upgradable gear slot inside the Forge Anvil modal.</summary>
    public int ResolveForgeTargetSlot(IWorldReadModel world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return _forgeModal.ResolveTargetSlot(world);
    }

    /// <summary>Gets a value indicating whether the status effect details panel is open (ADR-029).</summary>
    public bool IsStatusEffectDetailsOpen => _statusStrip.IsPanelOpen;

    /// <summary>
    /// Routes a left click to the top-right status strip: an icon opens its details, and any click closes an open panel.
    /// </summary>
    /// <returns>Whether the strip consumed the click.</returns>
    public bool TryHandleStatusStripClick(IWorldReadModel world, Vector2 mouseScreenPos, int viewportWidth)
    {
        ArgumentNullException.ThrowIfNull(world);
        (mouseScreenPos, viewportWidth) = this.ToCanvas(mouseScreenPos, viewportWidth);
        _statusStrip.ShowMinimap = this.Settings.ShowMinimap;
        return _statusStrip.HandleClick(mouseScreenPos, world.LocalStatusEffects, viewportWidth);
    }

    /// <summary>Gets whether <paramref name="mouseScreenPos"/> is over a status icon or the open details panel.</summary>
    public bool IsOverStatusStrip(IWorldReadModel world, Vector2 mouseScreenPos, int viewportWidth)
    {
        ArgumentNullException.ThrowIfNull(world);
        (mouseScreenPos, viewportWidth) = this.ToCanvas(mouseScreenPos, viewportWidth);
        _statusStrip.ShowMinimap = this.Settings.ShowMinimap;
        return _statusStrip.IsOver(mouseScreenPos, world.LocalStatusEffects, viewportWidth);
    }

    /// <summary>Closes the status effect details panel; returns whether it was open (Esc).</summary>
    public bool CloseStatusEffectDetails() => _statusStrip.Close();

    /// <summary>Gets the screen centre of status icon <paramref name="index"/> (screenshot previews).</summary>
    public Vector2 GetStatusIconCenter(int index, int viewportWidth)
    {
        (_, viewportWidth) = this.ToCanvas(Vector2.Zero, viewportWidth);
        RectangleF icon = StatusEffectStripLayout.Icon(index, viewportWidth, this.Settings.ShowMinimap);
        return _ui.ToWindow(new Vector2(icon.X + (icon.Width * 0.5f), icon.Y + (icon.Height * 0.5f)));
    }

    /// <summary>Opens the details of <paramref name="effectId"/> (screenshot previews).</summary>
    public void OpenStatusEffectDetails(ushort effectId) => _statusStrip.Open(effectId);

    /// <summary>Renders all screen-space RPG HUD widgets and active modals in strict 3-pass Z order.</summary>
    public void Render(
        IWorldReadModel world,
        SpriteBatch spriteBatch,
        int windowWidth,
        int windowHeight,
        float frameDt,
        Vector2? mouseWindowPos = null,
        CombatFeedbackTracker? feedback = null,
        Vector2? aimedWorldPos = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(spriteBatch);
        if (!_state.Visible || !world.IsReady || windowWidth <= 0 || windowHeight <= 0)
        {
            return;
        }

        // The HUD lays out on the shared UI-scale canvas (docs/art/menu-ui.md rule 21): every widget below works in canvas
        // units, the projection draws them Scale times larger and the mouse is mapped into the canvas here.
        _ui.Resize(windowWidth, windowHeight);
        int viewportWidth = _ui.CanvasWidth;
        int viewportHeight = _ui.CanvasHeight;
        Vector2? mouseScreenPos = mouseWindowPos is { } mouseWindow ? _ui.ToCanvas(mouseWindow) : null;

        _animTime += frameDt;
        _forgeModal.Update(frameDt, world);
        _toasts.Step(frameDt);
        if (frameDt > 0.0001f)
        {
            float instFps = Math.Clamp(1f / frameDt, 1f, 999f);
            _smoothedFps += (instFps - _smoothedFps) * 0.08f;
        }

        this.EnsureGpuResources();
        this.ReloadFontIfLanguageChanged();
        _targetWidget.UpdateTarget(world, feedback, aimedWorldPos);

        Matrix4x4 screenProj = Matrix4x4.CreateOrthographicOffCenter(0f, windowWidth / _ui.Scale, windowHeight / _ui.Scale, 0f, -1f, 1f);
        _uiSkin.PixelScale = _ui.Scale;
        BitmapFont hudFont = _ui.Font ?? _font;
        Vector2 playerPos = world.LocalPlayerInterpolatedPosition;
        float tileSize = world.ChunkWorld.TileSize;
        int ptx = (int)MathF.Floor(playerPos.X / tileSize);
        int pty = (int)MathF.Floor(playerPos.Y / tileSize);
        Vector2? bgMouse = _state.HasActiveModal ? null : mouseScreenPos;
        // The death screen goes on top (ADR-031): while dead the strip draws nothing and takes no clicks.
        _statusStrip.Suppressed = (_state.DeathPreview ?? world.LocalDeath) is not null;
        _statusStrip.ShowMinimap = this.Settings.ShowMinimap;
        _statusStrip.Prepare(world, hudFont, bgMouse, viewportWidth);
        _ = this.PrepareSkillBar(viewportWidth); // After the strip: the skill bar starts under its last row.

        _geometryBatch.Begin(screenProj);
        _vignette.DrawGeometry(_geometryBatch, world, viewportWidth, viewportHeight, _animTime);
        _geometryBatch.End();

        // Pass 1: Background HUD (NineSlice Skins -> Vector Geometry & Bars -> Bitmap Text), walked from HudComposition.MainHud.
        HudFrame frame = new(world, hudFont, viewportWidth, viewportHeight, ptx, pty, bgMouse, feedback);
        this.DrawElements(HudComposition.MainHud, spriteBatch, screenProj, in frame);

        // Pass 2 & 3: Active Modal Window & Floating Tooltip/Drag Layer
        if (_state.ShowSettingsModal)
        {
            _geometryBatch.Begin(screenProj);
            _geometryBatch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 6, 12, 150));
            _geometryBatch.End();

            spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
            _settingsModal.DrawSkin(spriteBatch, _uiSkin, this.Settings, viewportWidth, viewportHeight, mouseScreenPos);
            _settingsModal.DrawText(spriteBatch, hudFont, this.Settings, viewportWidth, viewportHeight);
            spriteBatch.End();
        }
        else if (_state.ShowPlayerModal)
        {
            _geometryBatch.Begin(screenProj);
            _geometryBatch.FillRectangle(new RectangleF(0f, 0f, viewportWidth, viewportHeight), new ColorRgba(4, 6, 12, 138));
            _geometryBatch.End();

            switch (_state.ActivePlayerTab)
            {
                case PlayerModalTab.Inventory:
                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _bagModal.DrawSkin(spriteBatch, _uiSkin, world, viewportWidth, viewportHeight, mouseScreenPos);
                    spriteBatch.End();

                    _geometryBatch.Begin(screenProj);
                    int hoveredModalSlot = _bagModal.DrawGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight, mouseScreenPos, _animTime);
                    _geometryBatch.End();

                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _bagModal.DrawText(spriteBatch, hudFont, world, viewportWidth, viewportHeight);
                    spriteBatch.End();

                    this.DrawElements(HudComposition.InventoryHeader, spriteBatch, screenProj, in frame);

                    _geometryBatch.Begin(screenProj);
                    _bagModal.DrawTopOverlayGeometry(_geometryBatch, world, _art, viewportWidth, viewportHeight, mouseScreenPos, hoveredModalSlot);
                    _geometryBatch.End();

                    spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: screenProj);
                    _bagModal.DrawTopOverlayText(spriteBatch, hudFont, world, viewportWidth, viewportHeight, mouseScreenPos, hoveredModalSlot);
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
                    _characterModal.DrawText(spriteBatch, hudFont, world, viewportWidth, viewportHeight, ptx, pty, _smoothedFps, this.Settings.ShowTelemetry);
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
                    _craftModal.DrawText(spriteBatch, hudFont, world, viewportWidth, viewportHeight);
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
                    _skillsModal.DrawText(spriteBatch, hudFont, world, viewportWidth, viewportHeight);
                    spriteBatch.End();
                    break;

                default:
                    break;
            }
        }
        else if (_state.Trader is TraderView trader)
        {
            TradeModalWidget.Render(spriteBatch, _geometryBatch, _uiSkin, hudFont, _art, screenProj, world, trader, _state.TradeBasket, _state.TradeDragSlot, viewportWidth, viewportHeight, mouseScreenPos);
        }
        else if (_state.Repair.IsOpen)
        {
            _repairWidget.Render(spriteBatch, _geometryBatch, _uiSkin, hudFont, _art, screenProj, world, _state.Repair, viewportWidth, viewportHeight, mouseScreenPos);
        }
        else if (_state.ShowForgeModal)
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
            _forgeModal.DrawText(spriteBatch, hudFont, world, viewportWidth, viewportHeight);
            spriteBatch.End();
        }

        // The death screen covers everything else while the player is dead (ADR-031).
        if ((_state.DeathPreview ?? world.LocalDeath) is DeathView death)
        {
            HudFrame deathFrame = frame with
            {
                BackgroundMouse = mouseScreenPos,
                Death = death,
                RespawnPending = _state.DeathPreview is null && world.IsRespawnPending,
            };
            this.DrawElements(HudComposition.DeathVeil, spriteBatch, screenProj, in deathFrame);
            this.DrawElements(HudComposition.DeathScreen, spriteBatch, screenProj, in deathFrame);
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
                _clock.DrawSkin(spriteBatch, _uiSkin, f.ViewportWidth, this.Settings.ShowMinimap, f.World.AuthoritativeTimeHour, f.World.AuthoritativeIsNight);
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
                _toasts.DrawSkin(spriteBatch, _uiSkin, f.Font, f.ViewportWidth, HudLayout.GetToastTop(f.ViewportWidth, this.Settings.ShowMinimap, _statusStrip.Rows));
                break;
            case HudElement.StatusEffects:
                _statusStrip.DrawSkin(spriteBatch, _uiSkin, f.World, f.ViewportWidth);
                break;
            case HudElement.DeathScreen:
                DeathScreenWidget.DrawSkin(spriteBatch, _uiSkin, f.RespawnPending, f.ViewportWidth, f.ViewportHeight, f.BackgroundMouse);
                break;
            case HudElement.InteractPrompt:
                if (!_state.HasActiveModal && _state.InteractPrompt is InteractionPrompt prompt)
                {
                    InteractionPrompt onCanvas = prompt with { ScreenAnchor = _ui.ToCanvas(prompt.ScreenAnchor) };
                    InteractionPromptWidget.DrawSkin(spriteBatch, _uiSkin, f.Font, in onCanvas);
                }

                break;
            case HudElement.Wallet:
            case HudElement.DeathVeil:
            case HudElement.UnderwaterVeil:
            case HudElement.OxygenBar:
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
                _clock.DrawGeometry(_geometryBatch, f.ViewportWidth, this.Settings.ShowMinimap, f.World.AuthoritativeTimeHour, f.World.AuthoritativeIsNight);
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
            case HudElement.StatusEffects:
                _statusStrip.DrawGeometry(_geometryBatch, f.World, _art, f.ViewportWidth);
                break;
            case HudElement.DeathVeil:
                DeathScreenWidget.DrawVeil(_geometryBatch, f.ViewportWidth, f.ViewportHeight);
                break;
            case HudElement.UnderwaterVeil:
                WaterView water = f.World.LocalWater;
                UnderwaterLook look = UnderwaterLook.For(in water);
                UnderwaterFilterWidget.DrawGeometry(_geometryBatch, in look, f.ViewportWidth, f.ViewportHeight, _animTime);
                break;
            case HudElement.OxygenBar:
                WaterView breath = f.World.LocalWater;
                OxygenBarWidget.DrawGeometry(_geometryBatch, in breath, f.ViewportWidth, f.ViewportHeight, _animTime);
                break;
            case HudElement.Toasts:
            case HudElement.DeathScreen:
            case HudElement.InteractPrompt:
            default:
                break;
        }
    }

    private void DrawElementText(HudElement element, SpriteBatch spriteBatch, in HudFrame f, int hoveredSlot, ushort hoveredSkill)
    {
        switch (element)
        {
            case HudElement.TargetFrame:
                _targetWidget.DrawText(spriteBatch, f.Font, _art, f.ViewportWidth);
                break;
            case HudElement.Clock:
                _clock.DrawText(spriteBatch, f.Font, f.ViewportWidth, this.Settings.ShowMinimap, f.World.AuthoritativeTimeHour, f.World.AuthoritativeIsNight);
                break;
            case HudElement.Minimap:
                if (this.Settings.ShowMinimap)
                {
                    _minimap.DrawText(spriteBatch, f.Font, f.ViewportWidth, f.Ptx, f.Pty, f.BackgroundMouse);
                }

                break;
            case HudElement.Hotbar:
                _hotbar.DrawText(spriteBatch, f.Font, f.World, _art, f.ViewportWidth, f.ViewportHeight, hoveredSlot);
                break;
            case HudElement.SkillBar:
                _skillBar.DrawText(spriteBatch, f.Font, f.World, f.ViewportWidth, f.ViewportHeight, hoveredSkill);
                break;
            case HudElement.Toasts:
                _toasts.DrawText(spriteBatch, f.Font, f.ViewportWidth, HudLayout.GetToastTop(f.ViewportWidth, this.Settings.ShowMinimap, _statusStrip.Rows));
                break;
            case HudElement.InteractPrompt:
                if (!_state.HasActiveModal && _state.InteractPrompt is InteractionPrompt prompt)
                {
                    InteractionPrompt onCanvas = prompt with { ScreenAnchor = _ui.ToCanvas(prompt.ScreenAnchor) };
                    InteractionPromptWidget.DrawText(spriteBatch, f.Font, in onCanvas);
                }

                break;
            case HudElement.StatusEffects:
                _statusStrip.DrawText(spriteBatch, f.Font, f.World, f.ViewportWidth);
                break;
            case HudElement.Wallet:
                PlayerModalLayout layout = PlayerModalTabs.ComputeLayout(f.ViewportWidth, f.ViewportHeight);
                WalletWidget.DrawText(spriteBatch, f.Font, f.World, in layout);
                break;
            case HudElement.DeathScreen when f.Death is DeathView death:
                _deathScreen.DrawText(spriteBatch, f.Font, in death, f.RespawnPending, f.ViewportWidth, f.ViewportHeight);
                break;
            case HudElement.OxygenBar:
                WaterView breath = f.World.LocalWater;
                _oxygenBar.DrawText(spriteBatch, f.Font, in breath, f.ViewportWidth, f.ViewportHeight);
                break;
            case HudElement.DeathVeil:
            case HudElement.DeathScreen:
            case HudElement.UnderwaterVeil:
            default:
                break;
        }
    }

    /// <summary>Per-frame inputs shared by every element of a draw list.</summary>
    private readonly record struct HudFrame(
        IWorldReadModel World,
        BitmapFont Font,
        int ViewportWidth,
        int ViewportHeight,
        int Ptx,
        int Pty,
        Vector2? BackgroundMouse,
        CombatFeedbackTracker? Feedback,
        DeathView? Death = null,
        bool RespawnPending = false);

    /// <summary>Maps a window-pixel point and window size onto the UI-scale canvas.</summary>
    private (Vector2 Point, int Width, int Height) ToCanvas(Vector2 windowPoint, int windowWidth, int windowHeight)
    {
        _ui.Resize(windowWidth, windowHeight);
        return (_ui.ToCanvas(windowPoint), _ui.CanvasWidth, _ui.CanvasHeight);
    }

    /// <summary>Maps a window-pixel point and window width onto the canvas, with the window height last seen (width-only callers).</summary>
    private (Vector2 Point, int Width) ToCanvas(Vector2 windowPoint, int windowWidth)
    {
        _ui.Resize(windowWidth, _ui.WindowHeight);
        return (_ui.ToCanvas(windowPoint), _ui.CanvasWidth);
    }

    /// <summary>Keeps the skill bar under the minimap/clock column and the status strip (<see cref="HudLayout"/>); always returns <see langword="true"/>.</summary>
    private bool PrepareSkillBar(int viewportWidth)
    {
        _skillBar.MinTop = HudLayout.GetSkillBarMinTop(viewportWidth, this.Settings.ShowMinimap, _statusStrip.Rows);
        return true;
    }

    // Creates the GPU resources once; later calls only read a flag, so frames after the first allocate nothing here.
    private void EnsureGpuResources()
    {
        if (_gpuReady)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        _geometryBatch = new GeometryBatch(_device);
        _fontLanguageVersion = GameText.Current.Version;
        (_font, _whitePixel) = GameFontLoader.Load(_device);
        IGraphicsDevice device = _device;
        _ui.EnsureFonts(() => new ScaledFontCache(scale => GameFontLoader.LoadFont(device, locale: null, scale)));
        _uiSkin = CarbonUiSkin.Load(_device);
        _gpuReady = true;
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
        _ui.Fonts?.Invalidate(); // the shared UI font re-bakes in the new language on its next use
        BitmapFont previous = _font;
        _font = GameFontLoader.LoadFont(_device);
        previous.Dispose();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            if (_gpuReady)
            {
                _uiSkin.Dispose();
            }

            _minimap.Dispose();
            if (_gpuReady)
            {
                _geometryBatch.Dispose();
                _font.Dispose();
                _whitePixel.Dispose();
            }

            if (_ownsUi)
            {
                _ui.Dispose();
            }

            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
