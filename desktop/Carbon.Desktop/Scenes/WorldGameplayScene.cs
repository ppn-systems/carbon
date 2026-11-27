// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Contracts.Common;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions.Configuration;
using Carbon.Desktop.Abstractions.Localization;
using Carbon.Desktop.Abstractions.Session;
using Carbon.Desktop.Abstractions.World;
using Carbon.Desktop.Capture;
using Carbon.Desktop.Gameplay.Input;
using Carbon.Desktop.Gameplay.Interaction;
using Carbon.Desktop.Gameplay.Repair;
using Carbon.Desktop.Gameplay.Trade;
using Carbon.Desktop.Input;
using Carbon.Desktop.Menu;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Items.Catalog;
using Carbon.Items.Containers;
using Carbon.Items.Definitions;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Navigation;
using Carbon.Maps.Streaming;
using Carbon.Maps.Topography;
using Silk.NET.OpenGL;
using Carbon.Desktop.Rendering.Camera;
using Carbon.Desktop.Rendering.World;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Primary playable 2.5D MMORPG world scene orchestrating input polling, <see cref="IWorldSession"/> updates,
/// <see cref="CameraRig2D"/> tracking, <see cref="WorldSceneRenderer"/> passes, <see cref="HudOverlayRenderer"/> HUD/Minimap,
/// Settings/SkillTome/Bag/Workshop/Forge modals, and capture hooks.
/// </summary>
public sealed class WorldGameplayScene : GameScene
{
    private readonly MenuFlow _flow;
    private readonly IWorldSession _session;
    private readonly DesktopOptions _options;
    private readonly ScreenshotAids _screenshotAids;
    private readonly Camera2D _camera;

    private readonly List<(int ChunkX, int ChunkY)> _dirtyChunksScratch = new(16);
    private readonly List<string> _notificationsScratch = new(16);

    private CameraRig2D? _cameraRig;
    private WorldSceneRenderer? _worldRenderer;
    private HudOverlayRenderer? _hudRenderer;
    private HudUiState _hudState = new(); // the HUD's window state; the renderer only reads it (audit OOP-3)
    private TradeInteraction? _trade;
    private RepairInteraction? _repair;
    private Task? _pendingLogoutTask;

    private BiomeType _lastBiome = (BiomeType)255;
    private byte _lastHeight = 255;
    private StructureType _lastStructure = StructureType.None;
    private (int X, int Y) _lastChunk = (int.MinValue, int.MinValue);
    private Vector2 _lastMoveDirection = Vector2.UnitX;
    private Vector2 _mouseScreenPosition;
    private Vector2 _mouseWorldPosition;
    private int _frameCount;
    private WindowTitleKey _lastTitleKey;
    private readonly AutoCapture _capture;
    private readonly InventoryInputController _inventoryInput;
    private readonly InteractionController _interaction;
    private readonly Func<Vector2, Vector2> _worldToScreen;
    private bool _forgePreviewStarted;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldGameplayScene"/> class.
    /// </summary>
    /// <param name="flow">Menu flow holding the initialized session, options and shared camera.</param>
    internal WorldGameplayScene(MenuFlow flow)
        : base("WorldGameplay")
    {
        _flow = flow ?? throw new ArgumentNullException(nameof(flow));
        IWorldSession session = flow.Session;
        DesktopOptions options = flow.Options;
        Camera2D camera = flow.Camera;
        _session = session;
        _options = options;
        _screenshotAids = new ScreenshotAids(_options);
        _capture = new AutoCapture(options);
        _inventoryInput = new InventoryInputController(_session);
        _interaction = new InteractionController(_session);
        _camera = camera;
        _worldToScreen = world => _camera.WorldToScreen(in world);
    }

    /// <inheritdoc/>
    public override void Load(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Load(context);

        _cameraRig = new CameraRig2D(_camera, _options.MacroView);
        _cameraRig.Resize(context.WindowWidth, context.WindowHeight);
        _worldRenderer = new WorldSceneRenderer(context.Device, _options);
        _hudState = new HudUiState();
        _hudRenderer = new HudOverlayRenderer(context.Device, _worldRenderer.VectorArt, _hudState);
        _inventoryInput.AttachHud(_hudRenderer);
        _repair = new RepairInteraction(_session, _hudState, _options);
        _trade = new TradeInteraction(_session, _hudRenderer, _options, IsTraderNpc, this.OpenForgeWindow, () => _repair.Open(RepairVenue.Blacksmith));
        if (!_options.MacroView)
        {
            this.ApplyZoomPreset(_hudRenderer.Settings.CameraZoomPreset);
        }

        _worldRenderer.EnsureWorldBound(_session);

        Vector2 focusPos = _session.LocalPlayerInterpolatedPosition;
        if (_options.FocusTileX.HasValue && _options.FocusTileY.HasValue)
        {
            focusPos = new Vector2((_options.FocusTileX.Value + 0.5f) * _options.TileSize, (_options.FocusTileY.Value + 0.5f) * _options.TileSize);
        }
        else if (_options.FocusBiome is BiomeType focusBiome)
        {
            ChunkWorld2D world = _session.ChunkWorld;
            (int sx, int sy) = world.SpawnTile;
            if (BiomeLocator.TryFind(world.Generator, focusBiome, sx, sy, 4000, out int bx, out int by))
            {
                focusPos = new Vector2((bx + 0.5f) * world.TileSize, (by + 0.5f) * world.TileSize);
                Console.WriteLine($"[Focus] {focusBiome} at tile {bx},{by}");
            }
            else
            {
                Console.WriteLine($"[Focus] No {focusBiome} tile within 4000 tiles of spawn");
            }
        }

        _cameraRig.SnapTo(focusPos);
    }

    /// <inheritdoc/>
    public override void Update(SceneContext context, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Update(context, dt);

        if (_pendingLogoutTask is not null)
        {
            if (_pendingLogoutTask.IsCompleted)
            {
                Task completed = _pendingLogoutTask;
                _pendingLogoutTask = null;
                try
                {
                    completed.GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Proceed cleanly back to the start screen.
                }

                StartScene? next = null;
                try
                {
                    next = new StartScene(_flow);
                    context.SceneManager.ChangeScene(next, transitionDuration: 0.16f);
                    next = null;
                }
                finally
                {
                    next?.Dispose();
                }
            }

            return;
        }

        if (!_session.IsReady || _cameraRig is null || _worldRenderer is null || _hudRenderer is null)
        {
            return;
        }

        _cameraRig.Resize(context.WindowWidth, context.WindowHeight);
        DesktopInputFrame input = _screenshotAids.PinMouse(DesktopInputController.Poll(context.Input));
        _mouseScreenPosition = _screenshotAids.Apply(_hudRenderer, _session, context.WindowWidth, context.WindowHeight, _worldRenderer.FeedbackTracker) ?? input.MouseScreenPosition;
        _mouseWorldPosition = _camera.ScreenToWorld(in _mouseScreenPosition);
        _worldRenderer.PreviewDowned = _hudState.DeathPreview is not null;

        if (DeathScreenInput.Handle(_session, input.ConfirmPressed, input.LeftClickPressed, input.MouseScreenPosition, context.WindowWidth, context.WindowHeight))
        {
            if (_hudState.HasActiveModal)
            {
                _hudState.CloseAllModals();
                _inventoryInput.ResetDragState();
            }

            _session.Update(dt, Vector2.Zero, sprint: false);
            return;
        }

        if (input.EscapePressed)
        {
            if (_hudRenderer.InventoryDialog.IsOpen)
            {
                _hudRenderer.InventoryDialog.Close();
            }
            else if (_hudRenderer.CloseStatusEffectDetails())
            {
                // ADR-029: Esc closes the status effect details first.
            }
            else if (_hudState.ShowSettingsModal)
            {
                _hudState.ShowSettingsModal = false;
            }
            else if (_hudState.HasActiveModal)
            {
                _hudState.CloseAllModals();
                _inventoryInput.ResetDragState();
            }
            else if (_session.LockedTargetNetworkId != 0UL)
            {
                _session.SetLockedTarget(0UL);
            }
            else
            {
                _hudState.ShowSettingsModal = true;
            }
        }

        _trade?.Sync(_frameCount);
        _repair?.Update((float)dt, _frameCount);

        // One target per frame: the E prompt is drawn over it and E acts on it (InteractionController).
        _ = _interaction.Refresh();
        if (_hudState.ShowSettingsModal)
        {
            if (input.LeftClickPressed)
            {
                Vector2 mouseScreen = input.MouseScreenPosition;
                if (!_hudRenderer.IsPointInActiveModal(_session, mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    _hudState.ShowSettingsModal = false;
                }
                else
                {
                    SettingsModalAction act = _hudRenderer.HandleSettingsClick(mouseScreen, context.WindowWidth, context.WindowHeight);
                    if (act == SettingsModalAction.Resume)
                    {
                        _hudState.ShowSettingsModal = false;
                    }
                    else if (act == SettingsModalAction.Logout)
                    {
                        _pendingLogoutTask = _flow.SignOutAsync();
                    }
                    else if (act == SettingsModalAction.Quit)
                    {
                        context.Window.Close();
                    }
                    else if (act == SettingsModalAction.ZoomChanged && !_options.MacroView)
                    {
                        this.ApplyZoomPreset(_hudRenderer.Settings.CameraZoomPreset);
                    }
                }
            }

            _session.Update(dt, Vector2.Zero, sprint: false);
            return;
        }

        if (input.ToggleHudPressed)
        {
            _hudState.Visible = !_hudState.Visible;
        }

        if (input.ToggleMinimapPressed && !_hudState.HasActiveModal)
        {
            _hudRenderer.Settings.ShowMinimap = !_hudRenderer.Settings.ShowMinimap;
            _hudRenderer.Settings.Save();
        }

        if (input.ToggleRestPressed && !_hudState.HasActiveModal)
        {
            _ = _session.TryToggleRest();
        }

        if (input.CycleTargetPressed && !_hudState.HasActiveModal)
        {
            _ = _session.CycleNearestHostileTarget();
        }

        if (input.ToggleInventoryPressed)
        {
            _inventoryInput.ResetDragState();
            _ = _hudState.OpenOrTogglePlayerTab(_session, PlayerModalTab.Inventory);
        }

        if (input.ToggleCharacterPressed)
        {
            _inventoryInput.ResetDragState();
            _ = _hudState.OpenOrTogglePlayerTab(_session, PlayerModalTab.Character);
        }

        if (input.ToggleSkillsPressed)
        {
            _inventoryInput.ResetDragState();
            if (!_hudState.OpenOrTogglePlayerTab(_session, PlayerModalTab.Skills))
            {
                _hudRenderer.PushNotification(GameText.Get("notice.skills_need_class"));
            }
        }

        if (input.ToggleCraftPressed)
        {
            _inventoryInput.ResetDragState();
            _ = _hudState.OpenOrTogglePlayerTab(_session, PlayerModalTab.Crafting);
        }

        if (input.ToggleForgePressed)
        {
            this.ToggleForgeStationModal();
        }

        if (input.ToggleRepairPressed)
        {
            _inventoryInput.ResetDragState();
            _repair?.Toggle();
        }

        if (input.MoveDirection.LengthSquared() > 1e-4f)
        {
            _lastMoveDirection = Vector2.Normalize(input.MoveDirection);
        }

        if (_options.ForgePreview is not null && !_forgePreviewStarted)
        {
            this.StartForgePreview(_options.ForgePreview);
        }

        if (_hudState.ShowForgeModal && _options.ForgePreview is null && !_session.IsNearForgeStation(185f))
        {
            _hudState.ShowForgeModal = false;
        }

        if (input.ScrollSlotDelta != 0)
        {
            if (_hudRenderer.InventoryDialog.IsOpen)
            {
                _hudRenderer.InventoryDialog.Adjust(-input.ScrollSlotDelta);
            }
            else if (_hudState.ShowPlayerModal && _hudState.ActivePlayerTab == PlayerModalTab.Crafting)
            {
                _hudRenderer.ScrollCrafting(input.ScrollSlotDelta);
            }
            else if (_hudState.ShowForgeModal)
            {
                _hudRenderer.ScrollForge(input.ScrollSlotDelta);
            }
            else if (_hudRenderer.TryZoomMinimap(input.MouseScreenPosition, -input.ScrollSlotDelta, context.WindowWidth))
            {
                // The wheel over the minimap zooms it instead of cycling the hotbar.
            }
            else if (!_hudState.HasActiveModal)
            {
                int nextSlot = (((_session.SelectedHotbarSlot + input.ScrollSlotDelta) % 9) + 9) % 9;
                _session.SelectHotbarSlot((byte)nextSlot);
            }
        }

        if (input.NumberSlotPressed is byte slotIdx && !_inventoryInput.TryBindHoveredSlotToHotbar(slotIdx, input.MouseScreenPosition, context.WindowWidth, context.WindowHeight))
        {
            _session.SelectHotbarSlot(slotIdx);
        }

        if (!_hudState.ShowPlayerModal || _hudState.ActivePlayerTab != PlayerModalTab.Inventory)
        {
            _hudRenderer.InventoryDialog.Close();
        }

        if (_hudState.ShowPlayerModal && _hudRenderer.InventoryDialog.IsOpen && _hudState.ActivePlayerTab == PlayerModalTab.Inventory)
        {
            _inventoryInput.HandleTabInput(in input, context.WindowWidth, context.WindowHeight);
            _hudState.CarriedInventorySlot = _inventoryInput.CarriedSlot;
        }
        else if (_hudState.ShowPlayerModal)
        {
            Vector2 mouseScreen = input.MouseScreenPosition;
            if (input.LeftClickPressed &&
                _hudRenderer.TryHitTestModalCloseButton(mouseScreen, context.WindowWidth, context.WindowHeight))
            {
                _hudState.ShowPlayerModal = false;
                _inventoryInput.ResetDragState();
            }
            else if (input.LeftClickPressed &&
                     _hudRenderer.TryHitTestPlayerModalTab(_session, mouseScreen, context.WindowWidth, context.WindowHeight, out PlayerModalTab nextTab))
            {
                _inventoryInput.ResetDragState();
                _hudState.ActivePlayerTab = nextTab;
            }
            else if (_hudState.ActivePlayerTab == PlayerModalTab.Inventory)
            {
                _inventoryInput.HandleTabInput(in input, context.WindowWidth, context.WindowHeight);
            }
            else if (_hudState.ActivePlayerTab == PlayerModalTab.Character)
            {
                if (input.LeftClickPressed)
                {
                    if (!_hudRenderer.IsPointInActiveModal(_session, mouseScreen, context.WindowWidth, context.WindowHeight))
                    {
                        _hudState.ShowPlayerModal = false;
                    }
                    else if (_hudRenderer.TryHitTestCharacterStatPlus(mouseScreen, context.WindowWidth, context.WindowHeight, out PrimaryAttributeId statTarget))
                    {
                        _ = _session.TryAllocateCharacterPoint(PointAllocationKind.Attribute, (ushort)statTarget, input.ShiftHeld ? (byte)5 : (byte)1); // Shift: 5 (server clamps)
                    }
                }
            }
            else if (_hudState.ActivePlayerTab == PlayerModalTab.Skills)
            {
                if (input.LeftClickPressed)
                {
                    if (!_hudRenderer.IsPointInActiveModal(_session, mouseScreen, context.WindowWidth, context.WindowHeight))
                    {
                        _hudState.ShowPlayerModal = false;
                    }
                    else if (_hudRenderer.TryHitTestSkillUpgrade(_session, mouseScreen, context.WindowWidth, context.WindowHeight, out ushort skillId))
                    {
                        _ = _session.TryAllocateCharacterPoint(PointAllocationKind.Skill, skillId, 1);
                    }
                }
            }
            else if (_hudState.ActivePlayerTab == PlayerModalTab.Crafting)
            {
                if (input.LeftClickPressed)
                {
                    if (!_hudRenderer.IsPointInActiveModal(_session, mouseScreen, context.WindowWidth, context.WindowHeight))
                    {
                        _hudState.ShowPlayerModal = false;
                    }
                    else if (_hudRenderer.TryHandleCraftingClick(mouseScreen, _session, context.WindowWidth, context.WindowHeight, out ushort recId, out ushort recQty) &&
                             recId != 0 && recQty != 0)
                    {
                        _ = _session.TryCraftRecipe(recId, recQty);
                    }
                }
            }

            _hudState.CarriedInventorySlot = _inventoryInput.CarriedSlot;
        }
        else if (_hudState.ShowForgeModal)
        {
            Vector2 mouseScreen = input.MouseScreenPosition;
            if (input.LeftClickPressed)
            {
                if (_hudRenderer.TryHitTestModalCloseButton(mouseScreen, context.WindowWidth, context.WindowHeight) ||
                    !_hudRenderer.IsPointInActiveModal(_session, mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    _hudState.ShowForgeModal = false;
                }
                else if (_hudRenderer.TryHitTestForgeAnvilButton(mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    int targetGear = _hudRenderer.ResolveForgeTargetSlot(_session);
                    if (targetGear >= 0 && !_hudRenderer.ForgeVfx.IsActive &&
                        _session.TryUpgradeInventoryItem((ushort)targetGear, _hudRenderer.ResolveForgeProtection(_session)))
                    {
                        // The hammer charges while the server rolls; the outcome effect plays when its result arrives.
                        _hudRenderer.ForgeVfx.BeginCharge();
                    }
                }
                else
                {
                    _ = _hudRenderer.TrySelectForgeGear(mouseScreen, _session, context.WindowWidth, context.WindowHeight);
                }
            }
        }
        else if (_hudState.Trader is not null)
        {
            _trade?.HandleWindowInput(input, context.WindowWidth, context.WindowHeight);
        }
        else if (_hudState.Repair.IsOpen)
        {
            _repair?.HandleWindowInput(input, context.WindowWidth, context.WindowHeight);
        }
        else
        {
            _inventoryInput.ResetDragState();

            if (input.LeftClickPressed)
            {
                Vector2 mouseScreen = input.MouseScreenPosition;
                if (_hudRenderer.TryHandleStatusStripClick(_session, mouseScreen, context.WindowWidth))
                {
                    // An icon opened its details, or a click outside closed them.
                }
                else if (_hudRenderer.TryHitTestHotbar(mouseScreen, context.WindowWidth, context.WindowHeight, out byte hotbarSlot))
                {
                    _session.SelectHotbarSlot(hotbarSlot);
                }
                else if (_hudRenderer.TryHitTestSkill(_session, mouseScreen, context.WindowWidth, context.WindowHeight, out ushort skillFromClick))
                {
                    _ = _session.TryCastSkill(skillFromClick, _mouseWorldPosition);
                }
                else
                {
                    Vector2 aimWorld = _camera.ScreenToWorld(in mouseScreen);
                    bool openedForgeNpc = _trade?.TryClick(aimWorld) ?? false;
                    IReadOnlyList<RemoteEntityView> remotes = _session.RemoteEntities;
                    for (int i = 0; !openedForgeNpc && i < remotes.Count; i++)
                    {
                        RemoteEntityView entity = remotes[i];
                        if (entity.Kind == EntityKind.Npc &&
                            WorldActorCatalog.GetNpcSpec(entity.ArchetypeId).Capabilities.HasFlag(NpcCapabilities.ForgeArtisan) &&
                            Vector2.DistanceSquared(aimWorld, entity.Position) <= (24f * 24f) &&
                            _session.IsNearForgeStation())
                        {
                            openedForgeNpc = this.OpenForgeWindow();
                            break;
                        }

                        if (entity.Kind != EntityKind.Monster || entity.CurrentHealth <= 0f)
                        {
                            continue;
                        }

                        float hitR = MathF.Max(22f, entity.Radius + 12f);
                        if (Vector2.DistanceSquared(aimWorld, entity.Position) <= hitR * hitR)
                        {
                            _session.SetLockedTarget(entity.NetworkId);
                            break;
                        }
                    }

                    if (!openedForgeNpc)
                    {
                        _ = _session.TriggerPrimaryAction(aimWorld, out _);
                    }
                }
            }
            else if (input.LeftMouseDown && !_session.CurrentAction.IsActive)
            {
                Vector2 mouseScreen = input.MouseScreenPosition;
                if (!_hudRenderer.IsOverStatusStrip(_session, mouseScreen, context.WindowWidth) &&
                    !_hudRenderer.TryHitTestHotbar(mouseScreen, context.WindowWidth, context.WindowHeight, out _) &&
                    !_hudRenderer.TryHitTestSkill(_session, mouseScreen, context.WindowWidth, context.WindowHeight, out _))
                {
                    Span<ItemSlot> curSlots = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
                    _session.CopyInventorySlots(curSlots);
                    ItemSlot active = _session.SelectedHotbarSlot < curSlots.Length ? curSlots[_session.SelectedHotbarSlot] : ItemSlot.Empty;
                    bool blockRepeat = !active.IsEmpty &&
                        _session.Items.Rules.Items.TryGet(active.ItemId, out ItemArchetype? activeDef) &&
                        activeDef.Category is ItemCategory.Consumable or ItemCategory.Placeable;
                    if (!blockRepeat)
                    {
                        Vector2 aimWorld = _camera.ScreenToWorld(in mouseScreen);
                        _ = _session.TriggerPrimaryAction(aimWorld, out _);
                    }
                }
            }
            else if (input.RightClickPressed &&
                     _hudRenderer.TryHitTestHotbar(input.MouseScreenPosition, context.WindowWidth, context.WindowHeight, out byte quickSlot))
            {
                _ = _session.TryQuickMoveInventorySlot(quickSlot);
            }

            if (input.SkillPressed is ushort skillId)
            {
                _ = _session.TryCastSkill(skillId, _mouseWorldPosition);
            }

            if (input.SpaceActionPressed)
            {
                Vector2 spaceAim = _session.LocalPlayerInterpolatedPosition + (_lastMoveDirection * (_session.ChunkWorld.TileSize * 1.25f));
                if (_session.LockedTargetNetworkId != 0UL)
                {
                    IReadOnlyList<RemoteEntityView> remotes = _session.RemoteEntities;
                    for (int i = 0; i < remotes.Count; i++)
                    {
                        if (remotes[i].NetworkId == _session.LockedTargetNetworkId)
                        {
                            spaceAim = remotes[i].Position;
                            Vector2 toTarget = spaceAim - _session.LocalPlayerInterpolatedPosition;
                            if (toTarget.LengthSquared() > 1e-4f)
                            {
                                _lastMoveDirection = Vector2.Normalize(toTarget);
                            }

                            break;
                        }
                    }
                }

                _ = _session.TriggerPrimaryAction(spaceAim, out _);
            }

            if (input.InteractOrPickupPressed)
            {
                _ = _interaction.TryInteract(this.OpenForgeWindow);
            }

            if (input.HarvestPressed)
            {
                _ = _session.TryHarvestNearbyProp(out _);
            }

            if (input.ToggleDoorPressed)
            {
                _ = _interaction.TryToggleNearestDoor();
            }

            if (input.PlaceStructurePressed)
            {
                _ = _session.TryPlaceStarterWall(out _);
            }

            if (input.DemolishStructurePressed ||
                (input.RightClickPressed &&
                 !_hudRenderer.TryHitTestHotbar(input.MouseScreenPosition, context.WindowWidth, context.WindowHeight, out _)))
            {
                _ = _session.TryDemolishStructure(_mouseWorldPosition, out _);
            }
        }

        if (input.DropItemPressed && _hudState.ShowPlayerModal && _inventoryInput.CarriedSlot >= 0 && !_hudRenderer.InventoryDialog.IsOpen)
        {
            _ = _inventoryInput.RequestDrop(_inventoryInput.CarriedSlot, 1);
            Span<ItemSlot> afterDrop = stackalloc ItemSlot[PlayerInventoryLayout.TotalCapacity];
            _session.CopyInventorySlots(afterDrop);
            if (_inventoryInput.CarriedSlot >= afterDrop.Length || afterDrop[_inventoryInput.CarriedSlot].Quantity <= 1)
            {
                _inventoryInput.ResetDragState();
            }
        }

        if (input.ToggleDebugPressed)
        {
            _worldRenderer.ShowDebugOverlay = !_worldRenderer.ShowDebugOverlay;
            Console.WriteLine($"[Debug] Solid collision overlay: {(_worldRenderer.ShowDebugOverlay ? "ON" : "OFF")}");
        }

        if (input.ScreenshotPressed)
        {
            AutoCapture.Capture(context.Window, _options.ScreenshotPath);
        }

        if (input.ZoomMultiplier.HasValue)
        {
            _cameraRig.AdjustZoom(input.ZoomMultiplier.Value);
        }

        Vector2 move = _options.AutoWalk is (float awx, float awy) ? new Vector2(awx, awy) : input.MoveDirection;
        _session.Update(dt, move, input.Sprint);

        _dirtyChunksScratch.Clear();
        _session.DrainDirtyChunks(_dirtyChunksScratch);
        for (int i = 0; i < _dirtyChunksScratch.Count; i++)
        {
            (int cx, int cy) = _dirtyChunksScratch[i];
            _worldRenderer.InvalidateChunk(cx, cy);
            _hudRenderer.InvalidateMinimapChunk(cx, cy);
        }

        _notificationsScratch.Clear();
        _session.DrainHudNotifications(_notificationsScratch);
        for (int i = 0; i < _notificationsScratch.Count; i++)
        {
            _hudRenderer.PushNotification(_notificationsScratch[i]);
        }

        Vector2 playerPos = _session.LocalPlayerInterpolatedPosition;
        float tileSize = _session.ChunkWorld.TileSize;
        int ptx = (int)MathF.Floor(playerPos.X / tileSize);
        int pty = (int)MathF.Floor(playerPos.Y / tileSize);

        this.UpdateTelemetry(context.Window, ptx, pty);

        bool inside = _worldRenderer.TryGetBuildingAt(playerPos, out RectangleF room);
        float rateDt = _options.CaptureFrames > 0 ? 1f / 15f : (float)dt;
        _cameraRig.Update(playerPos, inside, in room, rateDt);
    }

    /// <inheritdoc/>
    public override void Render(SceneContext context, SpriteBatch spriteBatch, double dt)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(spriteBatch);

        if (_cameraRig is null || _worldRenderer is null || _hudRenderer is null || !_session.IsReady)
        {
            return;
        }

        float frameDt = _options.CaptureFrames > 0 ? 1f / 15f : (float)dt;
        _worldRenderer.EnsureWorldBound(_session);
        _worldRenderer.QualityChoice = _hudRenderer.Settings.GraphicsQuality; // cosmetic only (ADR-041)
        _worldRenderer.Render(
            _session,
            _cameraRig,
            context.WindowWidth,
            context.WindowHeight,
            frameDt,
            _options.TimeScale,
            spriteBatch,
            _hudRenderer.Font,
            _mouseWorldPosition,
            _hudRenderer.Settings.ShowCombatNumbers);
        _hudRenderer.Settings.EffectiveGraphicsQuality = _worldRenderer.Quality;

        // No prompt while dead: Update stops refreshing the target then (ADR-031 death screen).
        _hudState.InteractPrompt = _session.LocalDeath is null ? _interaction.BuildPrompt(_worldToScreen) : null;
        _hudRenderer.Render(
            _session,
            spriteBatch,
            context.WindowWidth,
            context.WindowHeight,
            frameDt,
            _mouseScreenPosition,
            _worldRenderer.FeedbackTracker,
            _mouseWorldPosition);

        base.Render(context, spriteBatch, dt);

        _frameCount++;
        if (_frameCount == 15 && _options.CaptureModal.Length > 0)
        {
            CaptureModalOpener.Open(_hudRenderer, _session, _options.CaptureModal);
        }

        _capture.AfterFrame(context, _frameCount, _trade?.IsCaptureWaiting(_frameCount) == true || _repair?.IsCaptureWaiting(_frameCount) == true);

        GLEnum glError = context.Device.Gl.GetError();
        if (glError != GLEnum.NoError)
        {
            Console.WriteLine($"[OpenGL Error] {glError}");
        }
    }

    /// <inheritdoc/>
    public override void Resize(SceneContext context, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Resize(context, width, height);
        _cameraRig?.Resize(width, height);
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inventoryInput.AttachHud(null);
            _hudRenderer?.Dispose();
            _hudRenderer = null;
            _worldRenderer?.Dispose();
            _worldRenderer = null;
        }

        base.Dispose(disposing);
    }

    private void ApplyZoomPreset(int preset)
    {
        if (_cameraRig is null)
        {
            return;
        }

        float zoom = preset switch
        {
            0 => 1.55f,
            2 => 2.40f,
            _ => 1.95f,
        };
        _cameraRig.SetZoom(zoom);
    }

    /// <summary>
    /// Opens the forge window and freezes its animation on one outcome for screenshot review (<c>CARBON_FORGE_PREVIEW</c>).
    /// Visual only: no request is sent, so it cannot change any item.
    /// </summary>
    private void StartForgePreview(string spec)
    {
        _forgePreviewStarted = true;
        string[] parts = spec.Split(':', 2, StringSplitOptions.TrimEntries);
        EnhancementOutcome outcome = parts[0].ToUpperInvariant() switch
        {
            "SUCCESS" => EnhancementOutcome.Success,
            "NOCHANGE" => EnhancementOutcome.NoChange,
            "DOWNGRADE" => EnhancementOutcome.Downgrade,
            "DESTROYED" => EnhancementOutcome.Destroyed,
            _ => EnhancementOutcome.None,
        };
        float seconds = 0f;
        bool frozen = parts.Length == 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        _hudState.ShowForgeModal = true;
        _hudRenderer!.ForgeVfx.SfxCue += static cue => Console.WriteLine($"[ForgeSfx] {cue}");
        _hudRenderer.ForgeVfx.Preview(outcome, seconds, frozen);
        Console.WriteLine($"[ForgePreview] {outcome} {(frozen ? $"frozen at {seconds.ToString(CultureInfo.InvariantCulture)} s" : "playing")}");
    }

    private void ToggleForgeStationModal()
    {
        if (_hudRenderer is not null && _hudState.ShowForgeModal)
        {
            _hudState.ShowForgeModal = false;
            return;
        }

        _ = this.OpenForgeWindow();
    }

    /// <summary>
    /// Opens the forge enhancement window (ADR-024). This is the one entry point for every forge context: the
    /// <c>U</c> key, interacting at a placed Forge Anvil, and clicking any NPC whose spec has
    /// <see cref="NpcCapabilities.ForgeArtisan"/> (the sanctuary or a village blacksmith). The server accepts the enhance
    /// command near the same stations (<c>InventoryService.IsNearForgeStation</c>).
    /// </summary>
    /// <returns><see langword="true"/> when the window is open; <see langword="false"/> when no forge station is in reach.</returns>
    public bool OpenForgeWindow()
    {
        if (_hudRenderer is null)
        {
            return false;
        }

        if (!_session.IsNearForgeStation())
        {
            _hudRenderer.PushNotification(GameText.Get("forge.need_station"));
            return false;
        }

        _hudState.CloseAllModals();
        _inventoryInput.ResetDragState();
        _hudState.ShowForgeModal = true;
        return true;
    }

    /// <summary>Gets whether an NPC archetype trades (the village and sanctuary traders, ADR-025).</summary>
    private static bool IsTraderNpc(in RemoteEntityView entity) =>
        entity.Kind == EntityKind.Npc && WorldActorCatalog.GetNpcSpec(entity.ArchetypeId).Capabilities.HasFlag(NpcCapabilities.Trader);

    private void UpdateTelemetry(IGameWindow window, int ptx, int pty)
    {
        ChunkWorld2D chunkWorld = _session.ChunkWorld;
        MapTile currentTile = chunkWorld.GetTileAtWorld(ptx, pty);

        string biomeDisplayName = _worldRenderer?.VectorArt?.GetBiomeDisplayName((byte)currentTile.Biome) ?? currentTile.Biome.ToString();

        if (currentTile.Biome != _lastBiome)
        {
            _lastBiome = currentTile.Biome;
            Console.WriteLine($"[Biome] Entered: {biomeDisplayName,-16} (Speed: {currentTile.MoveSpeedMultiplier * 100:F0}%)");
        }

        if (currentTile.HeightLevel != _lastHeight)
        {
            _lastHeight = currentTile.HeightLevel;
            Console.WriteLine($"[Topography] Elevation level: {_lastHeight}");
        }

        if (chunkWorld.Buildings.TryGetStructure(ptx, pty, out StructureTile currentStructure))
        {
            if (currentStructure.Type != _lastStructure)
            {
                _lastStructure = currentStructure.Type;
                bool isRoofed = chunkWorld.Buildings.IsRoofedAt(ptx, pty);
                string structureName = _worldRenderer?.VectorArt is { } artRepo && artRepo.TryGetStructure((byte)currentStructure.Type, out Carbon.Desktop.Rendering.VectorArt.CompiledStructureArt? structArt)
                    ? structArt.DisplayName
                    : currentStructure.Type.ToString();
                Console.WriteLine($"[Structure] Entered: {structureName} | Roofed: {isRoofed}");
            }
        }
        else if (_lastStructure != StructureType.None)
        {
            _lastStructure = StructureType.None;
        }

        TileWalkability walkability = chunkWorld.NavGrid.GetWalkability(ptx, pty);
        RampDirection currentRamp = chunkWorld.NavGrid.GetRamp(ptx, pty);
        bool roofed = chunkWorld.Buildings.IsRoofedAt(ptx, pty);

        // The title is rebuilt (and sent to the OS) only when something it shows changes, not every frame
        // (audit round 2, PERF-6).
        WindowTitleKey titleKey = new(_session.ConnectionLabel, _session.ActiveCharacterName, biomeDisplayName, currentTile.HeightLevel, walkability, currentRamp, roofed, _session.RemoteEntities.Count);
        if (titleKey != _lastTitleKey)
        {
            _lastTitleKey = titleKey;
            string rampText = currentRamp != RampDirection.None ? $" [Ramp: {currentRamp}]" : string.Empty;
            string roofText = roofed ? " [Roofed Room]" : string.Empty;
            window.Title = $"Carbon MMORPG ({_session.ConnectionLabel}) | {_session.ActiveCharacterName} | Biome: {biomeDisplayName} | Alt: Lvl {currentTile.HeightLevel} | Nav: {walkability}{rampText}{roofText} | Peers: {_session.RemoteEntities.Count}";
        }

        int chunkX = Chunk2D.ToChunkCoord(ptx);
        int chunkY = Chunk2D.ToChunkCoord(pty);
        if (chunkX != _lastChunk.X || chunkY != _lastChunk.Y)
        {
            _lastChunk = (chunkX, chunkY);
            Console.WriteLine($"[ChunkStream] Chunk ({chunkX}, {chunkY}) | Active: {chunkWorld.LoadedChunkCount} | Pooled: {chunkWorld.PooledChunkCount}");
            _worldRenderer?.EvictDistant(chunkX, chunkY, 7);
        }
    }

    private readonly record struct WindowTitleKey(
        string? Transport,
        string? Character,
        string? Biome,
        int Height,
        TileWalkability Walkability,
        RampDirection Ramp,
        bool Roofed,
        int Peers);
}
