// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Input;
using Carbon.Desktop.Rendering;
using Carbon.Desktop.Rendering.Hud;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Navigation;
using Carbon.Maps.Streaming;
using Carbon.Maps.Topography;
using Silk.NET.OpenGL;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Primary playable 2.5D MMORPG world scene orchestrating input polling, <see cref="IClientWorldSession"/> updates,
/// <see cref="CameraRig2D"/> tracking, <see cref="WorldSceneRenderer"/> passes, <see cref="HudOverlayRenderer"/> HUD/Minimap,
/// Settings/SkillTome/Bag/Workshop/Forge modals, and capture hooks.
/// </summary>
public sealed class WorldGameplayScene : GameScene
{
    private readonly IClientWorldSession _session;
    private readonly DesktopClientOptions _options;
    private readonly Camera2D _camera;

    private readonly List<(int ChunkX, int ChunkY)> _dirtyChunksScratch = new(16);
    private readonly List<string> _notificationsScratch = new(16);

    private CameraRig2D? _cameraRig;
    private WorldSceneRenderer? _worldRenderer;
    private HudOverlayRenderer? _hudRenderer;
    private Task? _pendingLogoutTask;

    private BiomeType _lastBiome = (BiomeType)255;
    private byte _lastHeight = 255;
    private StructureType _lastStructure = StructureType.None;
    private (int X, int Y) _lastChunk = (int.MinValue, int.MinValue);
    private Vector2 _lastMoveDirection = Vector2.UnitX;
    private Vector2 _mouseScreenPosition;
    private Vector2 _mouseWorldPosition;
    private int _frameCount;
    private int _draggingInventorySlot = -1;
    private bool _hasAutoCaptured;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldGameplayScene"/> class.
    /// </summary>
    /// <param name="session">Initialized online client world session.</param>
    /// <param name="options">Client runtime options.</param>
    /// <param name="camera">Shared 2D orthographic camera.</param>
    public WorldGameplayScene(IClientWorldSession session, DesktopClientOptions options, Camera2D camera)
        : base("WorldGameplay")
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
    }

    /// <inheritdoc/>
    public override void Load(SceneContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        base.Load(context);

        _cameraRig = new CameraRig2D(_camera, _options.MacroView);
        _worldRenderer = new WorldSceneRenderer(context.Device, _options);
        _hudRenderer = new HudOverlayRenderer(context.Device, _worldRenderer.VectorArt);
        if (!_options.MacroView)
        {
            this.ApplyZoomPreset(_hudRenderer.Settings.CameraZoomPreset);
        }

        _worldRenderer.EnsureWorldBound(_session);

        Vector2 focusPos = _options.FocusTileX.HasValue && _options.FocusTileY.HasValue
            ? new Vector2((_options.FocusTileX.Value + 0.5f) * _options.TileSize, (_options.FocusTileY.Value + 0.5f) * _options.TileSize)
            : _session.LocalPlayerInterpolatedPosition;

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
                    // Proceed cleanly back to LoginScene.
                }

                LoginScene? next = null;
                try
                {
                    next = new LoginScene(_session, _options, _camera);
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

        DesktopInputFrame input = DesktopInputController.Poll(context.Input);
        _mouseScreenPosition = input.MouseScreenPosition;
        _mouseWorldPosition = _camera.ScreenToWorld(in _mouseScreenPosition);

        if (input.EscapePressed)
        {
            if (_hudRenderer.ShowSettingsModal)
            {
                _hudRenderer.ShowSettingsModal = false;
            }
            else if (_hudRenderer.HasActiveModal)
            {
                _hudRenderer.CloseAllModals();
                _draggingInventorySlot = -1;
            }
            else if (_session.LockedTargetNetworkId != 0UL)
            {
                _session.SetLockedTarget(0UL);
            }
            else
            {
                _hudRenderer.ShowSettingsModal = true;
            }
        }

        if (_hudRenderer.ShowSettingsModal)
        {
            if (input.LeftClickPressed)
            {
                Vector2 mouseScreen = input.MouseScreenPosition;
                if (!_hudRenderer.IsPointInActiveModal(mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    _hudRenderer.ShowSettingsModal = false;
                }
                else
                {
                    SettingsModalAction act = _hudRenderer.HandleSettingsClick(mouseScreen, context.WindowWidth, context.WindowHeight);
                    if (act == SettingsModalAction.Resume)
                    {
                        _hudRenderer.ShowSettingsModal = false;
                    }
                    else if (act == SettingsModalAction.Logout)
                    {
                        _pendingLogoutTask = _session.LogoutAsync();
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
            _hudRenderer.Visible = !_hudRenderer.Visible;
        }

        if (input.ToggleMinimapPressed && !_hudRenderer.HasActiveModal)
        {
            _hudRenderer.Settings.ShowMinimap = !_hudRenderer.Settings.ShowMinimap;
            _hudRenderer.Settings.Save();
        }

        if (input.CycleTargetPressed && !_hudRenderer.HasActiveModal)
        {
            _ = _session.CycleNearestHostileTarget();
        }

        if (input.ToggleInventoryPressed)
        {
            bool nextInv = !_hudRenderer.ShowInventoryModal;
            _hudRenderer.CloseAllModals();
            _draggingInventorySlot = -1;
            _hudRenderer.ShowInventoryModal = nextInv;
        }

        if (input.ToggleSkillsPressed)
        {
            bool nextSkills = !_hudRenderer.ShowSkillsModal;
            _hudRenderer.CloseAllModals();
            _draggingInventorySlot = -1;
            _hudRenderer.ShowSkillsModal = nextSkills;
        }

        if (input.ToggleCraftPressed)
        {
            bool nextCraft = !_hudRenderer.ShowCraftingPanel;
            _hudRenderer.CloseAllModals();
            _draggingInventorySlot = -1;
            _hudRenderer.ShowCraftingPanel = nextCraft;
        }

        if (input.ToggleForgePressed)
        {
            this.ToggleForgeStationModal();
        }

        if (input.MoveDirection.LengthSquared() > 1e-4f)
        {
            _lastMoveDirection = Vector2.Normalize(input.MoveDirection);
        }

        if (input.ScrollSlotDelta != 0)
        {
            if (_hudRenderer.ShowCraftingPanel)
            {
                _hudRenderer.ScrollCrafting(input.ScrollSlotDelta);
            }
            else if (!_hudRenderer.HasActiveModal)
            {
                int nextSlot = (((_session.SelectedHotbarSlot + input.ScrollSlotDelta) % 9) + 9) % 9;
                _session.SelectHotbarSlot((byte)nextSlot);
            }
        }

        if (input.NumberSlotPressed is byte slotIdx)
        {
            _session.SelectHotbarSlot(slotIdx);
        }

        if (_hudRenderer.ShowInventoryModal)
        {
            Vector2 mouseScreen = input.MouseScreenPosition;
            if (input.LeftClickPressed)
            {
                if (_hudRenderer.TryHitTestModalCloseButton(mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    _hudRenderer.ShowInventoryModal = false;
                    _hudRenderer.SelectedInventorySlot = -1;
                    _draggingInventorySlot = -1;
                }
                else if (_hudRenderer.TryHitTestInventoryStatPlus(mouseScreen, context.WindowWidth, context.WindowHeight, out CharacterPointTarget statTarget))
                {
                    _ = _session.TryAllocateCharacterPoint(statTarget, 1);
                }
                else if (_hudRenderer.TryHitTestInventoryUseEquipButton(mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    int targetSlot = _hudRenderer.ResolveInventoryActionTargetSlot(_session);
                    if (targetSlot >= 0)
                    {
                        if (targetSlot < 9)
                        {
                            _session.SelectHotbarSlot((byte)targetSlot);
                        }
                        else
                        {
                            _ = _session.TryQuickMoveInventorySlot((ushort)targetSlot);
                        }
                    }
                }
                else if (_hudRenderer.TryHitTestInventoryDropAllButton(mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    int targetSlot = _hudRenderer.ResolveInventoryActionTargetSlot(_session);
                    if (targetSlot >= 0)
                    {
                        _ = _session.TryDropInventoryItem((ushort)targetSlot, -1);
                    }
                }
                else if (_hudRenderer.TryHitTestInventoryDropButton(mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    int targetSlot = _hudRenderer.ResolveInventoryActionTargetSlot(_session);
                    if (targetSlot >= 0)
                    {
                        _ = _session.TryDropInventoryItem((ushort)targetSlot, 1);
                    }
                }
                else if (_hudRenderer.TryHitTestInventorySlot(mouseScreen, context.WindowWidth, context.WindowHeight, out ushort clickedSlot))
                {
                    if (input.ShiftHeld)
                    {
                        _ = _session.TryQuickMoveInventorySlot(clickedSlot);
                        _hudRenderer.SelectedInventorySlot = -1;
                        _draggingInventorySlot = -1;
                    }
                    else if (_hudRenderer.SelectedInventorySlot >= 0 && _hudRenderer.SelectedInventorySlot != clickedSlot)
                    {
                        _ = _session.TryMoveInventorySlot((ushort)_hudRenderer.SelectedInventorySlot, clickedSlot);
                        _hudRenderer.SelectedInventorySlot = -1;
                        _draggingInventorySlot = -1;
                    }
                    else
                    {
                        _hudRenderer.SelectedInventorySlot = clickedSlot;
                        _draggingInventorySlot = !_session.Inventory.GetSlot(clickedSlot).IsEmpty ? clickedSlot : -1;
                    }
                }
            }
            else if (!input.LeftMouseDown && _draggingInventorySlot >= 0)
            {
                if (_hudRenderer.TryHitTestInventorySlot(mouseScreen, context.WindowWidth, context.WindowHeight, out ushort dropTarget) &&
                    dropTarget != _draggingInventorySlot)
                {
                    _ = _session.TryMoveInventorySlot((ushort)_draggingInventorySlot, dropTarget);
                    _hudRenderer.SelectedInventorySlot = -1;
                }

                _draggingInventorySlot = -1;
            }
            else if (input.RightClickPressed &&
                     _hudRenderer.TryHitTestInventorySlot(mouseScreen, context.WindowWidth, context.WindowHeight, out ushort rightSlot))
            {
                _ = _session.TryQuickMoveInventorySlot(rightSlot);
                _hudRenderer.SelectedInventorySlot = -1;
                _draggingInventorySlot = -1;
            }

            _hudRenderer.DraggedInventorySlot = _draggingInventorySlot;
        }
        else if (_hudRenderer.ShowSkillsModal)
        {
            Vector2 mouseScreen = input.MouseScreenPosition;
            if (input.LeftClickPressed)
            {
                if (_hudRenderer.TryHitTestModalCloseButton(mouseScreen, context.WindowWidth, context.WindowHeight) ||
                    !_hudRenderer.IsPointInActiveModal(mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    _hudRenderer.ShowSkillsModal = false;
                }
                else if (_hudRenderer.TryHitTestSkillTomeUpgrade(mouseScreen, context.WindowWidth, context.WindowHeight, out CharacterPointTarget skillTarget))
                {
                    _ = _session.TryAllocateCharacterPoint(skillTarget, 1);
                }
            }
        }
        else if (_hudRenderer.ShowCraftingPanel)
        {
            Vector2 mouseScreen = input.MouseScreenPosition;
            if (input.LeftClickPressed)
            {
                if (_hudRenderer.TryHitTestModalCloseButton(mouseScreen, context.WindowWidth, context.WindowHeight) ||
                    !_hudRenderer.IsPointInActiveModal(mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    _hudRenderer.ShowCraftingPanel = false;
                }
                else if (_hudRenderer.TryHandleCraftingClick(mouseScreen, _session, context.WindowWidth, context.WindowHeight, out ushort recId, out ushort recQty) &&
                         recId != 0 && recQty != 0)
                {
                    for (int i = 0; i < recQty; i++)
                    {
                        if (!_session.TryCraftRecipe(recId))
                        {
                            break;
                        }
                    }
                }
            }
        }
        else if (_hudRenderer.ShowForgeModal)
        {
            Vector2 mouseScreen = input.MouseScreenPosition;
            if (input.LeftClickPressed)
            {
                if (_hudRenderer.TryHitTestModalCloseButton(mouseScreen, context.WindowWidth, context.WindowHeight) ||
                    !_hudRenderer.IsPointInActiveModal(mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    _hudRenderer.ShowForgeModal = false;
                }
                else if (_hudRenderer.TryHitTestForgeAnvilButton(mouseScreen, context.WindowWidth, context.WindowHeight))
                {
                    int targetGear = _hudRenderer.ResolveForgeTargetSlot(_session);
                    if (targetGear >= 0)
                    {
                        _ = _session.TryUpgradeInventoryItem((ushort)targetGear);
                    }
                }
                else
                {
                    _ = _hudRenderer.TrySelectForgeGear(mouseScreen, _session, context.WindowWidth, context.WindowHeight);
                }
            }
        }
        else
        {
            _draggingInventorySlot = -1;
            _hudRenderer.DraggedInventorySlot = -1;

            if (input.LeftClickPressed)
            {
                Vector2 mouseScreen = input.MouseScreenPosition;
                if (_hudRenderer.TryHitTestHudDockButton(mouseScreen, context.WindowWidth, context.WindowHeight, out HudDockAction dockAction))
                {
                    if (dockAction == HudDockAction.Inventory)
                    {
                        _hudRenderer.CloseAllModals();
                        _hudRenderer.ShowInventoryModal = true;
                    }
                    else if (dockAction == HudDockAction.Skills)
                    {
                        _hudRenderer.CloseAllModals();
                        _hudRenderer.ShowSkillsModal = true;
                    }
                    else if (dockAction == HudDockAction.Crafting)
                    {
                        _hudRenderer.CloseAllModals();
                        _hudRenderer.ShowCraftingPanel = true;
                    }
                    else if (dockAction == HudDockAction.Forge)
                    {
                        this.ToggleForgeStationModal();
                    }
                    else if (dockAction == HudDockAction.Settings)
                    {
                        _hudRenderer.CloseAllModals();
                        _hudRenderer.ShowSettingsModal = true;
                    }
                }
                else if (_hudRenderer.TryHitTestHotbar(mouseScreen, context.WindowWidth, context.WindowHeight, out byte hotbarSlot))
                {
                    _session.SelectHotbarSlot(hotbarSlot);
                }
                else if (_hudRenderer.TryHitTestSkill(mouseScreen, context.WindowWidth, context.WindowHeight, out byte skillFromClick))
                {
                    _ = _session.TryCastSkill(skillFromClick, _mouseWorldPosition);
                }
                else
                {
                    Vector2 aimWorld = _camera.ScreenToWorld(in mouseScreen);
                    bool openedForgeNpc = false;
                    IReadOnlyList<RemoteEntityView> remotes = _session.RemoteEntities;
                    for (int i = 0; i < remotes.Count; i++)
                    {
                        RemoteEntityView entity = remotes[i];
                        if (entity.Kind == Contracts.Entities.EntityKind.Npc &&
                            entity.ArchetypeId == 204 &&
                            Vector2.DistanceSquared(aimWorld, entity.Position) <= (24f * 24f) &&
                            _session.IsNearForgeStation())
                        {
                            this.ToggleForgeStationModal();
                            openedForgeNpc = true;
                            break;
                        }

                        if (entity.Kind != Contracts.Entities.EntityKind.Monster || entity.CurrentHealth <= 0f)
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
            else if (input.RightClickPressed &&
                     _hudRenderer.TryHitTestHotbar(input.MouseScreenPosition, context.WindowWidth, context.WindowHeight, out byte quickSlot))
            {
                _ = _session.TryQuickMoveInventorySlot(quickSlot);
            }

            if (input.SkillPressed is byte skillSlot)
            {
                _ = _session.TryCastSkill(skillSlot, _mouseWorldPosition);
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
                if (!_session.TryPickupNearbyLoot() && !_session.TryToggleNearbyDoor(out _) && _session.IsNearForgeStation())
                {
                    this.ToggleForgeStationModal();
                }
            }

            if (input.HarvestPressed)
            {
                _ = _session.TryHarvestNearbyProp(out _);
            }

            if (input.ToggleDoorPressed)
            {
                _ = _session.TryToggleNearbyDoor(out _);
            }

            if (input.PlaceHousePressed)
            {
                _ = _session.TryPlaceTestHouse(out _);
            }
        }

        if (input.DropItemPressed && _hudRenderer.ShowInventoryModal)
        {
            int targetSlot = _hudRenderer.ResolveInventoryActionTargetSlot(_session);
            if (targetSlot >= 0)
            {
                _ = _session.TryDropInventoryItem((ushort)targetSlot, 1);
            }
        }

        if (input.ToggleDebugPressed)
        {
            _worldRenderer.ShowDebugOverlay = !_worldRenderer.ShowDebugOverlay;
            Console.WriteLine($"[Debug] Solid collision overlay: {(_worldRenderer.ShowDebugOverlay ? "ON" : "OFF")}");
        }

        if (input.ScreenshotPressed)
        {
            this.CaptureScreenshot(context.Window, _options.ScreenshotPath);
        }

        if (input.ZoomMultiplier.HasValue)
        {
            _cameraRig.AdjustZoom(input.ZoomMultiplier.Value);
        }

        _session.Update(dt, input.MoveDirection, input.Sprint);

        _dirtyChunksScratch.Clear();
        _session.DrainDirtyChunks(_dirtyChunksScratch);
        for (int i = 0; i < _dirtyChunksScratch.Count; i++)
        {
            (int cx, int cy) = _dirtyChunksScratch[i];
            _worldRenderer.InvalidateChunk(cx, cy);
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

        if (_options.AutoExit && _frameCount >= 25 + _options.CaptureFrames)
        {
            context.Window.Close();
        }
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

        _hudRenderer.Render(
            _session,
            spriteBatch,
            context.WindowWidth,
            context.WindowHeight,
            frameDt,
            _worldRenderer.TimeOfDayHour,
            _mouseScreenPosition,
            _worldRenderer.FeedbackTracker,
            _mouseWorldPosition);

        base.Render(context, spriteBatch, dt);

        _frameCount++;
        if (_frameCount >= 20 && !_hasAutoCaptured)
        {
            _hasAutoCaptured = true;
            this.CaptureScreenshot(context.Window, _options.ScreenshotPath);
        }
        else if (_options.CaptureFrames > 0 && _frameCount > 20 && _frameCount <= 20 + _options.CaptureFrames)
        {
            string path = Path.Combine(
                Path.GetDirectoryName(_options.ScreenshotPath) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(_options.ScreenshotPath)}_{_frameCount - 20:D3}.bmp");
            ScreenshotHelper.Capture(context.Device.Gl, context.WindowWidth, context.WindowHeight, path);
        }

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
            0 => 0.85f,
            2 => 1.25f,
            _ => 1.0f,
        };
        _cameraRig.SetZoom(zoom);
    }

    private void ToggleForgeStationModal()
    {
        if (_hudRenderer is null)
        {
            return;
        }

        if (_hudRenderer.ShowForgeModal)
        {
            _hudRenderer.ShowForgeModal = false;
            return;
        }

        if (!_session.IsNearForgeStation())
        {
            _hudRenderer.PushNotification("Visit Master Blacksmith or place a Forge Anvil to enhance gear!");
            return;
        }

        _hudRenderer.CloseAllModals();
        _draggingInventorySlot = -1;
        _hudRenderer.ShowForgeModal = true;
    }

    private void UpdateTelemetry(IGameWindow window, int ptx, int pty)
    {
        ChunkWorld2D chunkWorld = _session.ChunkWorld;
        MapTile currentTile = chunkWorld.GetTileAtWorld(ptx, pty);

        if (currentTile.Biome != _lastBiome)
        {
            _lastBiome = currentTile.Biome;
            Console.WriteLine($"[Biome] Entered: {currentTile.Biome,-16} (Speed: {currentTile.MoveSpeedMultiplier * 100:F0}%)");
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
                Console.WriteLine($"[Structure] Entered: {currentStructure.Type} | Roofed: {isRoofed}");
            }
        }
        else if (_lastStructure != StructureType.None)
        {
            _lastStructure = StructureType.None;
        }

        TileWalkability walkability = chunkWorld.NavGrid.GetWalkability(ptx, pty);
        RampDirection currentRamp = chunkWorld.NavGrid.GetRamp(ptx, pty);
        string rampText = currentRamp != RampDirection.None ? $" [Ramp: {currentRamp}]" : string.Empty;
        string roofText = chunkWorld.Buildings.IsRoofedAt(ptx, pty) ? " [Roofed Room]" : string.Empty;
        window.Title = $"Carbon MMORPG ({_session.TransportLabel}) | {_session.ActiveCharacterName} | Biome: {currentTile.Biome} | Alt: Lvl {currentTile.HeightLevel} | Nav: {walkability}{rampText}{roofText} | Peers: {_session.RemoteEntities.Count}";

        int chunkX = Chunk2D.ToChunkCoord(ptx);
        int chunkY = Chunk2D.ToChunkCoord(pty);
        if (chunkX != _lastChunk.X || chunkY != _lastChunk.Y)
        {
            _lastChunk = (chunkX, chunkY);
            Console.WriteLine($"[ChunkStream] Chunk ({chunkX}, {chunkY}) | Active: {chunkWorld.LoadedChunkCount} | Pooled: {chunkWorld.PooledChunkCount}");
            _worldRenderer?.EvictDistant(chunkX, chunkY, 7);
        }
    }

    private void CaptureScreenshot(IGameWindow window, string filePath)
    {
        try
        {
            ScreenshotHelper.Capture(window.Device.Gl, window.Width, window.Height, filePath);
            Console.WriteLine($"[Screenshot] Saved capture to: {filePath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.WriteLine($"[Screenshot Error] {ex.Message}");
        }
    }
}
