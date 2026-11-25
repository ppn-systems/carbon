// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Carbon.Desktop.Rendering;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Configuration;
using Carbon.Graphics.Windowing.Window;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Landmarks;
using Carbon.Maps.Navigation;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
using Carbon.Maps.Topography;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Carbon.Desktop;

/// <summary>
/// Entry point for the Carbon native desktop client runner.
/// Demonstrates infinite procedural chunk streaming, massive macro-continental biomes,
/// zero-allocation chunk pooling, AABB frustum culling, and automated screenshot telemetry.
/// </summary>
public static class Program
{
    private const float TileSize = 32f;
    private const float MacroFrequency = 0.0008f; // Expansive continental scale (~1250 tiles per continent)
    // CARBON_SCREENSHOT_PATH overrides where captures are written (default: ./screenshots/world_capture.bmp).
    private static readonly string s_screenshotPath =
        Environment.GetEnvironmentVariable("CARBON_SCREENSHOT_PATH") is { Length: > 0 } customPath
            ? customPath
            : Path.Combine(Environment.CurrentDirectory, "screenshots", "world_capture.bmp");

    private static GeometryBatch? s_geometryBatch;
    private static PointOfInterestRenderer? s_poiRenderer;
    private static float s_animTime;
    private static Atmosphere? s_atmosphere;
    private static readonly float s_timeScale =
        float.TryParse(Environment.GetEnvironmentVariable("CARBON_TIME_SCALE"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float scale) ? Math.Clamp(scale, 0.1f, 1000f) : 1f;
    private static readonly List<PointLight> s_lights = new(256);
    private static readonly List<PointOfInterest> s_visibleWorldTrees = new();

    // Interior view: 0 = outside, 1 = fully inside a building (camera zoomed in, surroundings dimmed).
    private const float InteriorZoomBoost = 1.4f;
    private static float s_interiorBlend;
    private static RectangleF s_interiorRoom;
    private static float? s_userZoom;
    private static readonly int s_captureFrames =
        int.TryParse(Environment.GetEnvironmentVariable("CARBON_CAPTURE_FRAMES"), System.Globalization.CultureInfo.InvariantCulture, out int frames) ? Math.Clamp(frames, 0, 240) : 0;
    private static ChunkTerrainRenderer? s_chunkTerrainRenderer;
    private static Camera2D? s_camera;
    private static ChunkWorld2D? s_chunkWorld;
    private static WorldSimulation? s_world;
    private static Entity s_player = Entity.Null;
    private static BiomeType s_lastPlayerBiome = (BiomeType)255;
    private static byte s_lastPlayerHeight = 255;
    private static StructureType s_lastStructureType = StructureType.None;
    private static (int X, int Y) s_lastChunkCoord = (int.MinValue, int.MinValue);
    private static ulong s_currentSeed = 1337UL;
    private static bool s_debugOverlay;
    private static int s_frameCount;
    private static bool s_hasAutoCaptured;
    private static readonly List<GroundRenderItem> s_groundSortList = new(256);
    private static readonly List<CanopyRenderItem> s_canopySortList = new(128);

    /// <summary>
    /// Application main entry point.
    /// </summary>
    public static void Main()
    {
        Console.WriteLine("==========================================================================");
        Console.WriteLine("   Carbon MMORPG Infinite World — Macro Biomes, Rivers & Chunk Streaming  ");
        Console.WriteLine("==========================================================================");
        Console.WriteLine(" [W, A, S, D] / Arrows : Di chuyen nhan vat khap the gioi vo han");
        Console.WriteLine(" [Left Shift]          : Chay nhanh (Sprint 450 u/s)");
        Console.WriteLine(" [Q] / [E]             : Thu nho / Phong to camera (Zoom)");
        Console.WriteLine(" [R]                   : Tai sinh the gioi voi Seed ngau nhien moi");
        Console.WriteLine(" [B]                   : Bat / Tat hien thi vien va cham (Collision Overlay)");
        Console.WriteLine(" [H]                   : Dat thu nghiem nha RimWorld gan nhan vat (Desktop test)");
        Console.WriteLine(" [F12]                 : Chup anh man hinh (Screenshot to Artifact)");
        Console.WriteLine(" [Escape]              : Thoat khoi ung dung");
        Console.WriteLine("==========================================================================");

        WindowSettings settings = new()
        {
            Title = "Carbon MMORPG Open World — Infinite Chunk Streaming & Macro Biomes",
            Width = 1280,
            Height = 720,
            VSync = true
        };

        using GameWindow window = new(settings);

        window.Load += OnLoad;
        window.Update += OnUpdate;
        window.Render += OnRender;
        window.Resize += OnResize;
        window.Closing += OnClosing;

        window.Run();
    }

    private static void OnClosing(IGameWindow window)
    {
        s_groundSortList.Clear();
        s_canopySortList.Clear();
        s_chunkTerrainRenderer?.Dispose();
        s_chunkTerrainRenderer = null;
        s_geometryBatch?.Dispose();
        s_geometryBatch = null;
    }

    private static void OnLoad(IGameWindow window)
    {
        Console.WriteLine($"[OpenGL] Vendor:   {window.Device.Gl.GetStringS(StringName.Vendor)}");
        Console.WriteLine($"[OpenGL] Renderer: {window.Device.Gl.GetStringS(StringName.Renderer)}");
        Console.WriteLine($"[OpenGL] Version:  {window.Device.Gl.GetStringS(StringName.Version)}");

        s_geometryBatch = new GeometryBatch(window.Device);
        s_chunkTerrainRenderer = new ChunkTerrainRenderer(window.Device);
        s_camera = new Camera2D(window.Width, window.Height)
        {
            Zoom = Environment.GetEnvironmentVariable("CARBON_MACRO_VIEW") == "1" ? 0.25f : 1.0f
        };

        // CARBON_SEED selects the world seed (decimal), e.g. to reproduce a screenshot.
        if (ulong.TryParse(Environment.GetEnvironmentVariable("CARBON_SEED"), System.Globalization.CultureInfo.InvariantCulture, out ulong envSeed))
        {
            s_currentSeed = envSeed;
        }

        GenerateWorld(s_currentSeed);
    }

    private static void GenerateWorld(ulong seed)
    {
        s_currentSeed = seed;
        Console.WriteLine($"[WorldGen] Khoi tao the gioi vo han (Seed: 0x{seed:X16}, MacroFreq: {MacroFrequency:F4})...");

        ProceduralWorldGenerator generator = new(seed);
        s_chunkWorld = new ChunkWorld2D(generator, TileSize, MacroFrequency);
        s_poiRenderer = new PointOfInterestRenderer(TileSize, generator.Landmarks);

        // CARBON_TIME_OF_DAY (hour, 0-24), CARBON_DAY_LENGTH (real seconds per day) and CARBON_WEATHER
        // (rain | clear) pin the atmosphere, e.g. for screenshots.
        float startHour = float.TryParse(Environment.GetEnvironmentVariable("CARBON_TIME_OF_DAY"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hour) ? hour : 9f;
        float dayLength = float.TryParse(Environment.GetEnvironmentVariable("CARBON_DAY_LENGTH"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float length) ? length : 600f;
        bool? rain = Environment.GetEnvironmentVariable("CARBON_WEATHER") switch
        {
            "rain" => true,
            "clear" => false,
            _ => null,
        };
        s_atmosphere = new Atmosphere(startHour, dayLength, rain);
        s_chunkTerrainRenderer?.EvictDistant(0, 0, -1);

        // Preload chunks around the natural safe starter spawn
        Vector2 spawnPoint = s_chunkWorld.SpawnWorldPosition;

        // CARBON_FOCUS_TILE="x,y" starts the player at a given world tile (screenshots, debugging).
        if (Environment.GetEnvironmentVariable("CARBON_FOCUS_TILE") is { Length: > 0 } focus &&
            focus.Split(',') is [string fx, string fy] &&
            int.TryParse(fx, System.Globalization.CultureInfo.InvariantCulture, out int focusX) &&
            int.TryParse(fy, System.Globalization.CultureInfo.InvariantCulture, out int focusY))
        {
            spawnPoint = new Vector2((focusX + 0.5f) * TileSize, (focusY + 0.5f) * TileSize);
        }
        s_chunkWorld.UpdateAroundPosition(spawnPoint, chunkRadius: 4, evictionRadius: 8);

        s_world = new WorldSimulation(tickRate: 50, cellSize: TileSize)
        {
            WorldPhysics = s_chunkWorld
        };

        s_player = s_world.SpawnEntity(spawnPoint, radius: 7f);
        s_world.Entities.SetVelocity(s_player, new Velocity2D(0f, 0f, 5f));

        s_camera!.Position = spawnPoint;

        Console.WriteLine($"[WorldGen] Diem xuat phat tu nhien: ({spawnPoint.X:F1}, {spawnPoint.Y:F1})");
        Console.WriteLine($"[WorldGen] Chunks khoi tao: {s_chunkWorld.LoadedChunkCount} chunks san sang.");
    }

    private static void OnUpdate(IGameWindow window, double dt)
    {
        if (s_world == null || s_chunkWorld == null || s_camera == null || !s_world.Entities.IsAlive(s_player))
        {
            return;
        }

        // 1. Check escape
        if (window.Input.Keyboard.IsKeyDown(Key.Escape))
        {
            window.Close();
            return;
        }

        // 2. Map regeneration
        if (window.Input.Keyboard.IsKeyPressed(Key.R))
        {
            ulong nextSeed = (ulong)Environment.TickCount64 ^ 0xA5A5A5A5A5A5A5A5UL;
            GenerateWorld(nextSeed);
            return;
        }

        // 3. Toggle debug overlay
        if (window.Input.Keyboard.IsKeyPressed(Key.B))
        {
            s_debugOverlay = !s_debugOverlay;
            Console.WriteLine($"[Debug] Che do hien thi vat can va cham: {(s_debugOverlay ? "BAT" : "TAT")}");
        }

        // 4. Manual screenshot capture
        if (window.Input.Keyboard.IsKeyPressed(Key.F12))
        {
            CaptureScreen(window);
        }

        // 5. Desktop test building placement
        if (window.Input.Keyboard.IsKeyPressed(Key.H))
        {
            PlaceTestHouseNearPlayer();
        }

        // 5. Zoom controls
        // The player's chosen zoom is kept apart from the automatic interior zoom applied on top of it.
        s_userZoom ??= s_camera.Zoom;
        if (window.Input.Keyboard.IsKeyDown(Key.Q))
        {
            s_userZoom = Math.Clamp(s_userZoom.Value * 0.985f, 0.10f, 3.0f);
        }
        else if (window.Input.Keyboard.IsKeyDown(Key.E))
        {
            s_userZoom = Math.Clamp(s_userZoom.Value * 1.015f, 0.10f, 3.0f);
        }

        // 6. Movement controls
        Vector2 moveDir = Vector2.Zero;
        if (window.Input.Keyboard.IsKeyDown(Key.W) || window.Input.Keyboard.IsKeyDown(Key.Up))
        {
            moveDir.Y -= 1f;
        }

        if (window.Input.Keyboard.IsKeyDown(Key.S) || window.Input.Keyboard.IsKeyDown(Key.Down))
        {
            moveDir.Y += 1f;
        }

        if (window.Input.Keyboard.IsKeyDown(Key.A) || window.Input.Keyboard.IsKeyDown(Key.Left))
        {
            moveDir.X -= 1f;
        }

        if (window.Input.Keyboard.IsKeyDown(Key.D) || window.Input.Keyboard.IsKeyDown(Key.Right))
        {
            moveDir.X += 1f;
        }

        float baseSpeed = window.Input.Keyboard.IsKeyDown(Key.ShiftLeft) ? 450f : 250f;

        // Current terrain speed multiplier
        ref readonly Transform2D playerTrans = ref s_world.Entities.GetTransform(s_player);
        int ptx = (int)MathF.Floor(playerTrans.Position.X / TileSize);
        int pty = (int)MathF.Floor(playerTrans.Position.Y / TileSize);
        MapTile currentTile = s_chunkWorld.GetTileAtWorld(ptx, pty);

        if (moveDir.LengthSquared() > 0.001f)
        {
            s_world.EnqueueCommand(SimulationCommand.MoveInput(s_player, moveDir, baseSpeed));
        }

        // 7. Report biome, height level, ramp, and chunk transitions
        if (currentTile.Biome != s_lastPlayerBiome)
        {
            s_lastPlayerBiome = currentTile.Biome;
            Console.WriteLine($"[Biome] Buoc vao quan xa: {currentTile.Biome,-16} (Toc do di chuyen: {currentTile.MoveSpeedMultiplier * 100:F0}%)");
        }

        if (currentTile.HeightLevel != s_lastPlayerHeight)
        {
            s_lastPlayerHeight = currentTile.HeightLevel;
            string altName = currentTile.HeightLevel switch
            {
                0 => "Lowland (Vung thap)",
                1 => "Plateau (Cao nguyen / Doi)",
                2 => "Mountain Peak (Dinh nui)",
                _ => $"Level {currentTile.HeightLevel}"
            };
            Console.WriteLine($"[Topography] Tang cao do: {altName}");
        }

        TileWalkability walkability = s_chunkWorld.NavGrid.GetWalkability(ptx, pty);
        RampDirection currentRamp = s_chunkWorld.NavGrid.GetRamp(ptx, pty);

        if (s_chunkWorld.Buildings.TryGetStructure(ptx, pty, out StructureTile currentStructure))
        {
            if (currentStructure.Type != s_lastStructureType)
            {
                s_lastStructureType = currentStructure.Type;
                bool isRoofed = s_chunkWorld.Buildings.IsRoofedAt(ptx, pty);
                Console.WriteLine($"[Structure] Buoc vao kien truc: {currentStructure.Type} | Mai che: {isRoofed}");
            }
        }
        else if (s_lastStructureType != StructureType.None)
        {
            s_lastStructureType = StructureType.None;
        }

        // Real-time title bar HUD telemetry
        string rampText = currentRamp != RampDirection.None ? $" [Ramp: {currentRamp}]" : string.Empty;
        string roofText = s_chunkWorld.Buildings.IsRoofedAt(ptx, pty) ? " [Roofed Room]" : string.Empty;
        window.Title = $"Carbon MMORPG | Biome: {currentTile.Biome} | Alt: Lvl {currentTile.HeightLevel} | Nav: {walkability}{rampText}{roofText}";

        int currentChunkX = Chunk2D.ToChunkCoord(ptx);
        int currentChunkY = Chunk2D.ToChunkCoord(pty);
        if (currentChunkX != s_lastChunkCoord.X || currentChunkY != s_lastChunkCoord.Y)
        {
            s_lastChunkCoord = (currentChunkX, currentChunkY);
            Console.WriteLine($"[ChunkStream] Di chuyen vao Chunk ({currentChunkX}, {currentChunkY}) | Active Chunks: {s_chunkWorld.LoadedChunkCount} | Pooled: {s_chunkWorld.PooledChunkCount}");
            s_chunkTerrainRenderer?.EvictDistant(currentChunkX, currentChunkY, 7);
        }

        // 8. Stream infinite chunks around the moving player with pooling
        s_chunkWorld.UpdateAroundPosition(playerTrans.Position, chunkRadius: 4, evictionRadius: 7);

        // 9. Step simulation (drains input commands, applies terrain speed multiplier & CCD sub-stepping)
        _ = s_world.Update(dt);

        // 10. Follow player with camera
        ref readonly Transform2D updatedTrans = ref s_world.Entities.GetTransform(s_player);

        // 11. Interiors: entering a building eases the camera in on the room and fades the outside out.
        bool inside = s_poiRenderer is not null && s_poiRenderer.TryGetBuildingAt(updatedTrans.Position, out _, out s_interiorRoom);
        float target = inside ? 1f : 0f;
        float rate = s_captureFrames > 0 ? 1f / 15f : (float)dt;
        s_interiorBlend += (target - s_interiorBlend) * Math.Min(1f, rate * 4f);
        if (!inside && s_interiorBlend < 0.002f)
        {
            s_interiorBlend = 0f;
        }

        Vector2 roomCentre = new(s_interiorRoom.X + (s_interiorRoom.Width * 0.5f), s_interiorRoom.Y + (s_interiorRoom.Height * 0.5f));
        s_camera.Position = Vector2.Lerp(updatedTrans.Position, roomCentre, s_interiorBlend * 0.75f);
        s_camera.Zoom = (s_userZoom ?? s_camera.Zoom) * (1f + (InteriorZoomBoost * s_interiorBlend));

        if (s_frameCount >= 25 + s_captureFrames && Environment.GetEnvironmentVariable("CARBON_AUTO_EXIT") == "1")
        {
            window.Close();
        }
    }

    private static void OnRender(IGameWindow window, double dt)
    {
        // While capturing an animation, step time at a fixed 15 fps so the sequence is independent of the
        // (possibly slow, software-rendered) frame rate.
        float frameDt = s_captureFrames > 0 ? 1f / 15f : (float)dt;
        s_animTime += frameDt;
        // CARBON_TIME_SCALE speeds up the sky (clouds, weather, time of day), e.g. for time-lapse captures.
        s_atmosphere?.Update(frameDt * s_timeScale);
        s_lights.Clear();
        if (s_geometryBatch == null || s_camera == null || s_chunkWorld == null || s_world == null || s_chunkTerrainRenderer == null)
        {
            return;
        }

        window.Device.SetClearColor(new Vector4(0.04f, 0.05f, 0.08f, 1.0f));
        window.Device.Clear(ClearBufferMask.ColorBufferBit);

        float halfW = window.Width * 0.5f / s_camera.Zoom;
        float halfH = window.Height * 0.5f / s_camera.Zoom;

        float minX = s_camera.Position.X - halfW;
        float maxX = s_camera.Position.X + halfW;
        float minY = s_camera.Position.Y - halfH;
        float maxY = s_camera.Position.Y + halfH;

        // 1. Render Visible Chunks Terrain in 1 GPU Draw Call per Chunk (Static VBO Mesh Caching)
        s_chunkTerrainRenderer.Begin(s_camera.ViewProjectionMatrix);

        foreach (Chunk2D chunk in s_chunkWorld.ActiveChunks)
        {
            chunk.GetBounds(TileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);

            // Frustum Culling AABB test: skip entire chunk if outside camera view
            if (cMaxX < minX || cMinX > maxX || cMaxY < minY || cMinY > maxY)
            {
                continue;
            }

            s_chunkTerrainRenderer.RenderChunk(chunk, s_chunkWorld);
        }

        s_chunkTerrainRenderer.End();

        s_geometryBatch.Begin(s_camera.ViewProjectionMatrix);

        // Animated water over the baked waterfall curtains of the visible chunks
        foreach (Chunk2D chunk in s_chunkWorld.ActiveChunks)
        {
            chunk.GetBounds(TileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);
            if (cMaxX < minX || cMinX > maxX || cMaxY < minY || cMinY > maxY)
            {
                continue;
            }

            AmbientAnimator.DrawRipples(s_geometryBatch, s_chunkTerrainRenderer.GetRipples(chunk.Key), s_animTime);
            AmbientAnimator.DrawWaterfalls(s_geometryBatch, s_chunkTerrainRenderer.GetWaterfalls(chunk.Key), s_animTime);
        }

        // Cloud shadows drifting over the ground, under props and buildings
        s_atmosphere?.DrawCloudShadows(s_geometryBatch, minX, minY, maxX, maxY);

        // Debug overlay for solid terrain tiles
        if (s_debugOverlay)
        {
            foreach (Chunk2D chunk in s_chunkWorld.ActiveChunks)
            {
                chunk.GetBounds(TileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);
                if (cMaxX < minX || cMinX > maxX || cMaxY < minY || cMinY > maxY)
                {
                    continue;
                }

                for (int ly = 0; ly < Chunk2D.Size; ly++)
                {
                    float tileWorldY = cMinY + (ly * TileSize);
                    for (int lx = 0; lx < Chunk2D.Size; lx++)
                    {
                        ref readonly MapTile tile = ref chunk.GetTile(lx, ly);
                        if (tile.IsSolid)
                        {
                            float tileWorldX = cMinX + (lx * TileSize);
                            RectangleF innerBorder = new(tileWorldX + 2f, tileWorldY + 2f, TileSize - 4f, TileSize - 4f);
                            s_geometryBatch.FillRectangle(innerBorder, new ColorRgba(0, 0, 0, 70));
                        }
                    }
                }
            }
        }

        Vector2 playerPos = s_world.GetInterpolatedPosition(s_player);

        // 2. Collect visible ground props & entities into zero-alloc pooled sort list (Layer 2: 2.5D Y-Sorting)
        s_groundSortList.Clear();
        s_canopySortList.Clear();

        s_visibleWorldTrees.Clear();

        // Points of interest: flat ground layer now (under props and entities), upright pieces Y-sorted.
        // Culled by footprint rather than by chunk, since a village spills over its centre chunk.
        foreach (Chunk2D chunk in s_chunkWorld.ActiveChunks)
        {
            for (int i = 0; i < chunk.PointsOfInterest.Count; i++)
            {
                PointOfInterest poi = chunk.PointsOfInterest[i];
                float reach = (poi.Radius + 6) * TileSize * 2f;
                float px = (poi.TileX + 0.5f) * TileSize;
                float py = (poi.TileY + 0.5f) * TileSize;
                if (px + reach < minX || px - reach > maxX || py + reach < minY || py - reach > maxY || s_poiRenderer is null)
                {
                    continue;
                }

                s_poiRenderer.DrawGround(s_geometryBatch, poi, s_animTime, playerPos);
                s_poiRenderer.CollectLights(poi, s_lights, s_animTime);
                if (poi.Type == PointOfInterestType.WorldTree)
                {
                    s_visibleWorldTrees.Add(poi);
                }
                ReadOnlySpan<LandmarkPiece> pieces = s_poiRenderer.GetLayout(poi).Pieces;
                for (int k = 0; k < pieces.Length; k++)
                {
                    s_groundSortList.Add(new GroundRenderItem(pieces[k].Y * TileSize, pieces[k]));
                }
            }
        }

        foreach (Chunk2D chunk in s_chunkWorld.ActiveChunks)
        {
            chunk.GetBounds(TileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);
            if (cMaxX < minX || cMinX > maxX || cMaxY < minY || cMinY > maxY)
            {
                continue;
            }

            for (int i = 0; i < chunk.Props.Count; i++)
            {
                MapProp prop = chunk.Props[i];
                if (prop.WorldPositionX + 96f >= minX && prop.WorldPositionX - 96f <= maxX &&
                    prop.WorldPositionY + 96f >= minY && prop.WorldPositionY - 96f <= maxY)
                {
                    s_groundSortList.Add(new GroundRenderItem(prop.GroundFootprintY, in prop));
                    if (PropArt.TryGetLight(prop, out PointLight propLight))
                    {
                        s_lights.Add(propLight);
                    }

                    if (prop.HasCanopy)
                    {
                        s_canopySortList.Add(new CanopyRenderItem(prop.WorldPositionY, in prop));
                    }
                }
            }
        }

        for (int i = 0; i < s_world.Entities.ActiveEntities.Count; i++)
        {
            Entity entity = s_world.Entities.ActiveEntities[i];
            if (!s_world.Entities.HasTransform(entity) || !s_world.Entities.HasCollider(entity))
            {
                continue;
            }

            ref readonly Transform2D trans = ref s_world.Entities.GetTransform(entity);
            ref readonly Collider2D col = ref s_world.Entities.GetCollider(entity);

            float entityFootY = trans.Position.Y + col.Radius;
            s_groundSortList.Add(new GroundRenderItem(entityFootY, entity));
        }

        // 3. Sort Ground Items by Foot Y in Ascending Order (North-to-South 2.5D Depth Sorting)
        s_groundSortList.Sort();

        Span<GroundRenderItem> groundSpan = CollectionsMarshal.AsSpan(s_groundSortList);
        for (int i = 0; i < groundSpan.Length; i++)
        {
            ref readonly GroundRenderItem item = ref groundSpan[i];
            if (item.IsPiece)
            {
                s_poiRenderer?.DrawPiece(s_geometryBatch, item.Piece, s_animTime, playerPos);
            }
            else if (item.IsProp)
            {
                RenderPropGround(s_geometryBatch, item.Prop);
            }
            else
            {
                RenderEntity(s_geometryBatch, item.Entity);
            }
        }

        // 4. Render Overhead Tree Canopies with X-Ray Fading (Layer 3: Canopy Occlusion in Y-Order)
        s_canopySortList.Sort();
        Span<CanopyRenderItem> canopySpan = CollectionsMarshal.AsSpan(s_canopySortList);
        for (int i = 0; i < canopySpan.Length; i++)
        {
            ref readonly CanopyRenderItem item = ref canopySpan[i];
            // Canopies sway in the wind (trunks stay put): per-tree phase plus a slow shared gust.
            // The sway is a drawing offset only: the prop itself (and so its look) is never modified.
            MapProp tree = item.Prop;
            float gust = 0.6f + (0.4f * MathF.Sin(s_animTime * 0.35f));
            float sway = MathF.Sin((s_animTime * 1.25f) + (tree.WorldPositionX * 0.013f) + (tree.WorldPositionY * 0.007f)) * 1.8f * gust * tree.Scale;
            RenderPropCanopy(s_geometryBatch, tree, playerPos, sway);
        }

        // World Tree canopies tower over everything
        foreach (PointOfInterest tree in s_visibleWorldTrees)
        {
            s_poiRenderer?.DrawWorldTreeCanopy(s_geometryBatch, tree, s_animTime, playerPos);
        }

        // Atmosphere: ambient light (multiply), then point lights (additive), then clouds, rain, fireflies
        if (s_atmosphere is not null)
        {
            s_geometryBatch.End();

            s_geometryBatch.Begin(s_camera.ViewProjectionMatrix, Atmosphere.Multiply);
            s_atmosphere.DrawAmbient(s_geometryBatch, minX, minY, maxX, maxY);
            s_geometryBatch.End();

            // The player carries a soft lantern glow after dark
            s_lights.Add(new PointLight(playerPos, 150f, new ColorRgba(255, 214, 150), 0.6f));
            s_geometryBatch.Begin(s_camera.ViewProjectionMatrix, Carbon.Graphics.OpenGL.States.BlendState.Additive);
            s_atmosphere.DrawLights(s_geometryBatch, s_lights);
            s_geometryBatch.End();

            s_geometryBatch.Begin(s_camera.ViewProjectionMatrix);
            if (s_interiorBlend < 0.5f)
            {
                s_atmosphere.DrawOverlay(s_geometryBatch, minX, minY, maxX, maxY, playerPos);
            }
        }

        // Interior view: darken everything outside the room the player stands in
        if (s_interiorBlend > 0.01f)
        {
            RectangleF room = s_interiorRoom;
            float pad = 4f;
            float left = room.X - pad;
            float right = room.X + room.Width + pad;
            float topY = room.Y - pad;
            float bottom = room.Y + room.Height + pad;
            ColorRgba dim = new(6, 8, 14, (byte)(225f * s_interiorBlend));
            float bigL = minX - 64f;
            float bigT = minY - 64f;
            float bigR = maxX + 64f;
            float bigB = maxY + 64f;
            s_geometryBatch.FillRectangle(new RectangleF(bigL, bigT, bigR - bigL, topY - bigT), dim);
            s_geometryBatch.FillRectangle(new RectangleF(bigL, bottom, bigR - bigL, bigB - bottom), dim);
            s_geometryBatch.FillRectangle(new RectangleF(bigL, topY, left - bigL, bottom - topY), dim);
            s_geometryBatch.FillRectangle(new RectangleF(right, topY, bigR - right, bottom - topY), dim);

            // Soft inner glow along the edge of the darkness
            ColorRgba edge = new(6, 8, 14, (byte)(90f * s_interiorBlend));
            s_geometryBatch.FillRectangle(new RectangleF(left, topY, right - left, 6f), edge);
            s_geometryBatch.FillRectangle(new RectangleF(left, bottom - 6f, right - left, 6f), edge);
            s_geometryBatch.FillRectangle(new RectangleF(left, topY, 6f, bottom - topY), edge);
            s_geometryBatch.FillRectangle(new RectangleF(right - 6f, topY, 6f, bottom - topY), edge);
        }

        s_geometryBatch.End();

        // 5. Automated Screenshot Telemetry (captures frame 20 for analysis)
        s_frameCount++;
        if (s_frameCount >= 20 && !s_hasAutoCaptured)
        {
            s_hasAutoCaptured = true;
            CaptureScreen(window);
        }
        else if (s_captureFrames > 0 && s_frameCount > 20 && s_frameCount <= 20 + s_captureFrames)
        {
            // CARBON_CAPTURE_FRAMES=N also saves the N following frames as <name>_001.bmp ... for animations.
            string path = Path.Combine(
                Path.GetDirectoryName(s_screenshotPath) ?? string.Empty,
                $"{Path.GetFileNameWithoutExtension(s_screenshotPath)}_{s_frameCount - 20:D3}.bmp");
            ScreenshotHelper.Capture(window.Device.Gl, window.Width, window.Height, path);
        }

        GLEnum glError = window.Device.Gl.GetError();
        if (glError != GLEnum.NoError)
        {
            Console.WriteLine($"[OpenGL Error] {glError}");
        }
    }

    private static void CaptureScreen(IGameWindow window)
    {
        try
        {
            ScreenshotHelper.Capture(window.Device.Gl, window.Width, window.Height, s_screenshotPath);
            Console.WriteLine($"[Screenshot] Da luu anh chup man hinh vao: {s_screenshotPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.WriteLine($"[Screenshot Error] {ex.Message}");
        }
    }

    // Natural props are drawn by PropArt, which varies every instance (palette, leaf layout, lean, crystals).
    private static void RenderPropGround(GeometryBatch batch, in MapProp prop) => PropArt.DrawGround(batch, prop);

    private static void RenderPropCanopy(GeometryBatch batch, in MapProp prop, Vector2 playerPosition, float swayX)
    {
        // When the player stands under a canopy, the leaves fade so the player and the trunk stay visible.
        byte alpha = prop.IsUnderCanopy(playerPosition) ? (byte)90 : (byte)255;
        PropArt.DrawCanopy(batch, prop, alpha, swayX);
    }

    private static void RenderEntity(GeometryBatch batch, Entity entity)
    {
        if (s_world == null || s_chunkWorld == null)
        {
            return;
        }

        if (!s_world.Entities.HasTransform(entity) || !s_world.Entities.HasCollider(entity))
        {
            return;
        }

        Vector2 entityPos = s_world.GetInterpolatedPosition(entity);
        ref readonly Collider2D col = ref s_world.Entities.GetCollider(entity);

        if (entity == s_player)
        {
            // Current player tile for contextual foot effects (water splash / ripple)
            int ptx = (int)MathF.Floor(entityPos.X / TileSize);
            int pty = (int)MathF.Floor(entityPos.Y / TileSize);
            MapTile currentTile = s_chunkWorld.GetTileAtWorld(ptx, pty);

            if (currentTile.Biome is BiomeType.ShallowWater)
            {
                // Water ripple ring around player's feet when wading in shallow water
                batch.FillCircle(entityPos, col.Radius + 6f, new ColorRgba(255, 255, 255, 55));
                batch.FillCircle(entityPos, col.Radius + 4f, new ColorRgba(56, 189, 248, 75));
            }

            // Player: High-contrast sleek MMORPG hero avatar token
            // Drop shadow
            batch.FillCircle(new Vector2(entityPos.X + 2f, entityPos.Y + 3f), col.Radius + 1f, new ColorRgba(0, 0, 0, 75));
            // High-contrast platinum outer rim (ensures crisp visibility against dark ocean or night tiles)
            batch.FillCircle(entityPos, col.Radius + 1.5f, new ColorRgba(248, 250, 252, 220));
            // Outer navy mantle
            batch.FillCircle(entityPos, col.Radius, new ColorRgba(15, 23, 42, 255));
            // Royal blue tunic
            batch.FillCircle(entityPos, col.Radius * 0.82f, new ColorRgba(30, 64, 175, 255));
            // Cyan energy ring
            batch.FillCircle(entityPos, col.Radius * 0.52f, new ColorRgba(56, 189, 248, 255));
            // Golden core crest
            batch.FillCircle(entityPos, col.Radius * 0.22f, new ColorRgba(250, 204, 21, 255));
        }
    }

    private static void OnResize(IGameWindow window, int width, int height)
    {
        if (s_camera != null && width > 0 && height > 0)
        {
            s_camera.ViewportSize = new Vector2(width, height);
        }
    }

    private static void PlaceTestHouseNearPlayer()
    {
        if (s_chunkWorld == null || s_world == null || !s_world.Entities.IsAlive(s_player))
        {
            return;
        }

        Vector2 playerPos = s_world.GetInterpolatedPosition(s_player);
        int ptx = (int)MathF.Floor(playerPos.X / TileSize) + 4;
        int pty = (int)MathF.Floor(playerPos.Y / TileSize);

        for (int hy = 0; hy < 5; hy++)
        {
            for (int hx = 0; hx < 5; hx++)
            {
                int wx = ptx + hx;
                int wy = pty + hy;
                bool isEdge = hx == 0 || hx == 4 || hy == 0 || hy == 4;
                if (isEdge)
                {
                    if (hx == 2 && hy == 4)
                    {
                        s_chunkWorld.Buildings.ForcePlaceStructure(wx, wy, StructureType.WoodDoor);
                    }
                    else
                    {
                        s_chunkWorld.Buildings.ForcePlaceStructure(wx, wy, StructureType.StoneWall);
                    }
                }
                else
                {
                    s_chunkWorld.Buildings.ForcePlaceStructure(wx, wy, StructureType.WoodFloor);
                }
            }
        }

        s_chunkTerrainRenderer?.EvictDistant((int)MathF.Floor(playerPos.X / (32 * TileSize)), (int)MathF.Floor(playerPos.Y / (32 * TileSize)), 0);
        Console.WriteLine($"[Test] Da dung thu nghiem 1 can nha RimWorld tai ({ptx}, {pty}) bang phim [H]!");
    }

    private readonly struct GroundRenderItem : IComparable<GroundRenderItem>
    {
        public readonly float SortY;
        public readonly bool IsProp;
        public readonly MapProp Prop;
        public readonly Entity Entity;
        public readonly bool IsPiece;
        public readonly LandmarkPiece Piece;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GroundRenderItem(float sortY, in MapProp prop)
        {
            SortY = sortY;
            IsProp = true;
            Prop = prop;
            Entity = Entity.Null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GroundRenderItem(float sortY, Entity entity)
        {
            SortY = sortY;
            IsProp = false;
            Prop = default;
            Entity = entity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GroundRenderItem(float sortY, in LandmarkPiece piece)
        {
            SortY = sortY;
            IsProp = false;
            Prop = default;
            Entity = Entity.Null;
            IsPiece = true;
            Piece = piece;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(GroundRenderItem other) => SortY.CompareTo(other.SortY);
    }

    private readonly struct CanopyRenderItem : IComparable<CanopyRenderItem>
    {
        public readonly float SortY;
        public readonly MapProp Prop;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CanopyRenderItem(float sortY, in MapProp prop)
        {
            SortY = sortY;
            Prop = prop;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(CanopyRenderItem other) => SortY.CompareTo(other.SortY);
    }
}
