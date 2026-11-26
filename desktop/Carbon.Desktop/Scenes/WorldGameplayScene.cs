// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Carbon.Desktop.Abstractions;
using Carbon.Desktop.Input;
using Carbon.Desktop.Rendering;
using Carbon.Graphics.Batching;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Layout;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Graphics.Scenes.Base;
using Carbon.Graphics.Scenes.Management;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Input;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Navigation;
using Carbon.Maps.Streaming;
using Carbon.Maps.Topography;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Carbon.Desktop.Scenes;

/// <summary>
/// Primary playable 2.5D MMORPG world scene orchestrating input polling, <see cref="IClientWorldSession"/> updates,
/// <see cref="CameraRig2D"/> tracking, <see cref="WorldSceneRenderer"/> passes, <see cref="HudOverlayRenderer"/> HUD/Minimap,
/// pause/logout navigation, and capture hooks.
/// </summary>
public sealed class WorldGameplayScene : GameScene
{
    private readonly IClientWorldSession _session;
    private readonly DesktopClientOptions _options;
    private readonly Camera2D _camera;

    private readonly MenuButton _resumeButton = new("RESUME GAME", MenuButtonStyle.SecondaryEmerald);
    private readonly MenuButton _logoutButton = new("LOGOUT / SWITCH HERO", MenuButtonStyle.PrimaryGold);
    private readonly MenuButton _quitButton = new("QUIT DESKTOP CLIENT", MenuButtonStyle.DangerRed);
    private readonly List<(int ChunkX, int ChunkY)> _dirtyChunksScratch = new(16);
    private readonly List<string> _notificationsScratch = new(16);

    private CameraRig2D? _cameraRig;
    private WorldSceneRenderer? _worldRenderer;
    private HudOverlayRenderer? _hudRenderer;
    private GeometryBatch? _menuBatch;
    private Task? _pendingLogoutTask;

    private BiomeType _lastBiome = (BiomeType)255;
    private byte _lastHeight = 255;
    private StructureType _lastStructure = StructureType.None;
    private (int X, int Y) _lastChunk = (int.MinValue, int.MinValue);
    private Vector2 _lastMoveDirection = Vector2.UnitX;
    private Vector2 _mouseScreenPosition;
    private Vector2 _mouseWorldPosition;
    private int _frameCount;
    private bool _hasAutoCaptured;
    private bool _showEscapeMenu;

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
        _menuBatch = new GeometryBatch(context.Device);
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
            if (_hudRenderer.ShowCraftingPanel)
            {
                _hudRenderer.ShowCraftingPanel = false;
            }
            else
            {
                _showEscapeMenu = !_showEscapeMenu;
            }
        }

        if (_showEscapeMenu)
        {
            this.UpdatePauseMenu(context, input);
            _session.Update(dt, Vector2.Zero, sprint: false);
            return;
        }

        if (input.ToggleHudPressed)
        {
            _hudRenderer.Visible = !_hudRenderer.Visible;
        }

        if (input.ToggleCraftPressed)
        {
            _hudRenderer.ShowCraftingPanel = !_hudRenderer.ShowCraftingPanel;
        }

        if (input.MoveDirection.LengthSquared() > 1e-4f)
        {
            _lastMoveDirection = Vector2.Normalize(input.MoveDirection);
        }

        if (input.ScrollSlotDelta != 0)
        {
            int nextSlot = (((_session.SelectedHotbarSlot + input.ScrollSlotDelta) % 9) + 9) % 9;
            _session.SelectHotbarSlot((byte)nextSlot);
        }

        if (input.NumberSlotPressed is byte slotIdx)
        {
            if (_hudRenderer.ShowCraftingPanel && slotIdx < 6)
            {
                _ = _session.TryCraftRecipe((ushort)(slotIdx + 1));
            }
            else
            {
                _session.SelectHotbarSlot(slotIdx);
            }
        }

        if (input.LeftClickPressed)
        {
            Vector2 mouseScreen = input.MouseScreenPosition;
            if (_hudRenderer.TryHitTestHotbar(mouseScreen, context.WindowWidth, context.WindowHeight, out byte hotbarSlot))
            {
                _session.SelectHotbarSlot(hotbarSlot);
            }
            else if (_hudRenderer.TryHitTestCraftingRecipe(mouseScreen, context.WindowWidth, context.WindowHeight, out ushort craftRecipeId))
            {
                _ = _session.TryCraftRecipe(craftRecipeId);
            }
            else if (!_hudRenderer.IsPointInCraftingPanel(mouseScreen, context.WindowWidth, context.WindowHeight))
            {
                Vector2 aimWorld = _camera.ScreenToWorld(in mouseScreen);
                if (_session.TriggerPrimaryAction(aimWorld, out (int ChunkX, int ChunkY) clickChunk))
                {
                    _worldRenderer.InvalidateChunk(clickChunk.ChunkX, clickChunk.ChunkY);
                }
            }
        }

        if (input.SpaceActionPressed)
        {
            Vector2 spaceAim = _session.LocalPlayerInterpolatedPosition + (_lastMoveDirection * (_session.ChunkWorld.TileSize * 1.25f));
            if (_session.TriggerPrimaryAction(spaceAim, out (int ChunkX, int ChunkY) spaceChunk))
            {
                _worldRenderer.InvalidateChunk(spaceChunk.ChunkX, spaceChunk.ChunkY);
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

        if (input.HarvestPressed && _session.TryHarvestNearbyProp(out (int ChunkX, int ChunkY) harvestChunk))
        {
            _worldRenderer.InvalidateChunk(harvestChunk.ChunkX, harvestChunk.ChunkY);
        }

        if (input.ToggleDoorPressed && _session.TryToggleNearbyDoor(out (int ChunkX, int ChunkY) doorChunk))
        {
            _worldRenderer.InvalidateChunk(doorChunk.ChunkX, doorChunk.ChunkY);
        }

        if (input.PlaceHousePressed && _session.TryPlaceTestHouse(out (int ChunkX, int ChunkY) houseChunk))
        {
            _worldRenderer.InvalidateChunk(houseChunk.ChunkX, houseChunk.ChunkY);
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
            _mouseWorldPosition);

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

        if (_showEscapeMenu && _menuBatch is not null)
        {
            this.RenderPauseOverlay(spriteBatch, context.WindowWidth, context.WindowHeight);
        }

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
            _menuBatch?.Dispose();
            _menuBatch = null;
            _hudRenderer?.Dispose();
            _hudRenderer = null;
            _worldRenderer?.Dispose();
            _worldRenderer = null;
        }

        base.Dispose(disposing);
    }

    private void UpdatePauseMenu(SceneContext context, in DesktopInputFrame input)
    {
        this.LayoutPauseMenu(context.WindowWidth, context.WindowHeight);

        MouseSnapshot mouse = context.Input.Mouse;
        bool leftDown = mouse.IsButtonDown(MouseButton.Left);
        bool leftPressed = input.LeftClickPressed;
        Vector2 mousePos = input.MouseScreenPosition;

        if (_resumeButton.Update(mousePos, leftDown, leftPressed) || input.NumberSlotPressed == 0)
        {
            _showEscapeMenu = false;
        }
        else if (_logoutButton.Update(mousePos, leftDown, leftPressed) || input.NumberSlotPressed == 1)
        {
            _pendingLogoutTask = _session.LogoutAsync();
        }
        else if (_quitButton.Update(mousePos, leftDown, leftPressed) || input.NumberSlotPressed == 2)
        {
            context.Window.Close();
        }
    }

    private void LayoutPauseMenu(int width, int height)
    {
        const float cardW = 340f;
        const float cardH = 240f;
        float x = MathF.Floor((width - cardW) * 0.5f);
        float y = MathF.Floor((height - cardH) * 0.5f);

        _resumeButton.Bounds = new RectangleF(x + 24f, y + 58f, cardW - 48f, 44f);
        _logoutButton.Bounds = new RectangleF(x + 24f, y + 114f, cardW - 48f, 44f);
        _quitButton.Bounds = new RectangleF(x + 24f, y + 170f, cardW - 48f, 44f);
    }

    private void RenderPauseOverlay(SpriteBatch spriteBatch, int width, int height)
    {
        if (_menuBatch is null || _hudRenderer is null || width <= 0 || height <= 0)
        {
            return;
        }

        this.LayoutPauseMenu(width, height);
        Matrix4x4 proj = Matrix4x4.CreateOrthographicOffCenter(0f, width, height, 0f, -1f, 1f);

        const float cardW = 340f;
        const float cardH = 240f;
        float x = MathF.Floor((width - cardW) * 0.5f);
        float y = MathF.Floor((height - cardH) * 0.5f);

        _menuBatch.Begin(proj);
        _menuBatch.FillRectangle(new RectangleF(0f, 0f, width, height), new ColorRgba(6, 10, 20, 145));
        _menuBatch.FillRectangle(new RectangleF(x, y, cardW, cardH), new ColorRgba(10, 15, 28, 240));
        _menuBatch.DrawRectangle(new RectangleF(x, y, cardW, cardH), new ColorRgba(250, 204, 21, 230), 2f);
        _resumeButton.DrawGeometry(_menuBatch);
        _logoutButton.DrawGeometry(_menuBatch);
        _quitButton.DrawGeometry(_menuBatch);
        _menuBatch.End();

        spriteBatch.Begin(DepthSortMode.Immediate, transformMatrix: proj);
        TextMeasureResult titleSize = _hudRenderer.Font.MeasureString("SYSTEM MENU", TextLayoutOptions.Default, 2.5f);
        spriteBatch.DrawOutlinedString(
            _hudRenderer.Font,
            "SYSTEM MENU",
            new Vector2(MathF.Floor(x + ((cardW - titleSize.Width) * 0.5f)), y + 18f),
            new ColorRgba(250, 204, 21, 255),
            new ColorRgba(8, 10, 16, 255),
            1,
            2.5f);
        _resumeButton.DrawText(spriteBatch, _hudRenderer.Font);
        _logoutButton.DrawText(spriteBatch, _hudRenderer.Font);
        _quitButton.DrawText(spriteBatch, _hudRenderer.Font);
        spriteBatch.End();
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
