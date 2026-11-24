// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
using Carbon.Desktop.Rendering;
using Carbon.Graphics.Batching.Camera;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Windowing.Abstractions;
using Carbon.Graphics.Windowing.Configuration;
using Carbon.Graphics.Windowing.Window;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Maps.Streaming;
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
    private static readonly string s_screenshotPath = Path.Combine(
        @"C:\Users\nguye\.gemini\antigravity-cli\brain\16aec843-7ea2-45f0-ac90-80492879a105",
        "world_capture.bmp");

    private static GeometryBatch? s_geometryBatch;
    private static ChunkTerrainRenderer? s_chunkTerrainRenderer;
    private static Camera2D? s_camera;
    private static ChunkWorld2D? s_chunkWorld;
    private static WorldSimulation? s_world;
    private static Entity s_player = Entity.Null;
    private static BiomeType s_lastPlayerBiome = (BiomeType)255;
    private static (int X, int Y) s_lastChunkCoord = (int.MinValue, int.MinValue);
    private static ulong s_currentSeed = 1337UL;
    private static bool s_debugOverlay;
    private static int s_frameCount;
    private static bool s_hasAutoCaptured;

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
            Zoom = 1.0f
        };

        GenerateWorld(s_currentSeed);
    }

    private static void GenerateWorld(ulong seed)
    {
        s_currentSeed = seed;
        Console.WriteLine($"[WorldGen] Khoi tao the gioi vo han (Seed: 0x{seed:X16}, MacroFreq: {MacroFrequency:F4})...");

        ProceduralWorldGenerator generator = new(seed);
        s_chunkWorld = new ChunkWorld2D(generator, TileSize, MacroFrequency);
        s_chunkTerrainRenderer?.EvictDistant(0, 0, -1);

        // Preload chunks around the natural safe starter spawn
        Vector2 spawnPoint = s_chunkWorld.SpawnWorldPosition;
        s_chunkWorld.UpdateAroundPosition(spawnPoint, chunkRadius: 4, evictionRadius: 8);

        s_world = new WorldSimulation(tickRate: 50, cellSize: TileSize);

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

        // 5. Zoom controls
        if (window.Input.Keyboard.IsKeyDown(Key.Q))
        {
            s_camera.Zoom = Math.Clamp(s_camera.Zoom * 0.985f, 0.10f, 3.0f);
        }
        else if (window.Input.Keyboard.IsKeyDown(Key.E))
        {
            s_camera.Zoom = Math.Clamp(s_camera.Zoom * 1.015f, 0.10f, 3.0f);
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

        float finalSpeed = baseSpeed * currentTile.MoveSpeedMultiplier;

        if (moveDir.LengthSquared() > 0.001f)
        {
            moveDir = Vector2.Normalize(moveDir);
            ref Velocity2D vel = ref s_world.Entities.GetVelocity(s_player);
            vel.Linear = moveDir * finalSpeed;
        }

        // 7. Report biome & chunk transitions
        if (currentTile.Biome != s_lastPlayerBiome)
        {
            s_lastPlayerBiome = currentTile.Biome;
            Console.WriteLine($"[Biome] Buoc vao quan xa: {currentTile.Biome,-16} (Toc do di chuyen: {currentTile.MoveSpeedMultiplier * 100:F0}%)");
        }

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

        // 9. Step simulation
        _ = s_world.Update(dt);

        // 10. Continuous physical collision sliding (terrain, trunks, ores)
        ref Transform2D simTrans = ref s_world.Entities.GetTransform(s_player);
        ref readonly Collider2D col = ref s_world.Entities.GetCollider(s_player);
        Vector2 resolvedPos = simTrans.Position;
        if (s_chunkWorld.ResolveEntityCollision(ref resolvedPos, col.Radius, out Vector2 pushNormal))
        {
            simTrans.Position = resolvedPos;
            ref Velocity2D vel = ref s_world.Entities.GetVelocity(s_player);
            float dot = Vector2.Dot(vel.Linear, pushNormal);
            if (dot < 0f)
            {
                vel.Linear -= pushNormal * dot;
            }
        }

        // 11. Follow player with camera
        ref readonly Transform2D updatedTrans = ref s_world.Entities.GetTransform(s_player);
        s_camera.Position = updatedTrans.Position;

        if (s_frameCount >= 25 && Environment.GetEnvironmentVariable("CARBON_AUTO_EXIT") == "1")
        {
            window.Close();
        }
    }

    private static void OnRender(IGameWindow window, double dt)
    {
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

        Vector2 playerPos = s_world.Entities.GetTransform(s_player).Position;

        // 2. Render Props Ground Layer (Trunks, Minerals, Bushes, Ground Obstacles)
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
                    RenderPropGround(s_geometryBatch, prop);
                }
            }
        }

        // 3. Render Active Entities (Layer 2.5: Player Avatar)
        for (int i = 0; i < s_world.Entities.ActiveEntities.Count; i++)
        {
            Entity entity = s_world.Entities.ActiveEntities[i];
            if (!s_world.Entities.HasTransform(entity) || !s_world.Entities.HasCollider(entity))
            {
                continue;
            }

            ref readonly Transform2D trans = ref s_world.Entities.GetTransform(entity);
            ref readonly Collider2D col = ref s_world.Entities.GetCollider(entity);

            if (entity == s_player)
            {
                // Current player tile for contextual foot effects (water splash / ripple)
                int ptx = (int)MathF.Floor(trans.Position.X / TileSize);
                int pty = (int)MathF.Floor(trans.Position.Y / TileSize);
                MapTile currentTile = s_chunkWorld.GetTileAtWorld(ptx, pty);

                if (currentTile.Biome is BiomeType.ShallowWater)
                {
                    // Water ripple ring around player's feet when wading in shallow water
                    s_geometryBatch.FillCircle(trans.Position, col.Radius + 6f, new ColorRgba(255, 255, 255, 55));
                    s_geometryBatch.FillCircle(trans.Position, col.Radius + 4f, new ColorRgba(56, 189, 248, 75));
                }

                // Player: High-contrast sleek MMORPG hero avatar token
                // Drop shadow
                s_geometryBatch.FillCircle(new Vector2(trans.Position.X + 2f, trans.Position.Y + 3f), col.Radius + 1f, new ColorRgba(0, 0, 0, 75));
                // High-contrast platinum outer rim (ensures crisp visibility against dark ocean or night tiles)
                s_geometryBatch.FillCircle(trans.Position, col.Radius + 1.5f, new ColorRgba(248, 250, 252, 220));
                // Outer navy mantle
                s_geometryBatch.FillCircle(trans.Position, col.Radius, new ColorRgba(15, 23, 42, 255));
                // Royal blue tunic
                s_geometryBatch.FillCircle(trans.Position, col.Radius * 0.82f, new ColorRgba(30, 64, 175, 255));
                // Cyan energy ring
                s_geometryBatch.FillCircle(trans.Position, col.Radius * 0.52f, new ColorRgba(56, 189, 248, 255));
                // Golden core crest
                s_geometryBatch.FillCircle(trans.Position, col.Radius * 0.22f, new ColorRgba(250, 204, 21, 255));
            }
        }

        // 4. Render Overhead Tree Canopies with X-Ray Fading (Layer 3: Canopy Occlusion)
        // When player walks beneath tree shade, foliage fades to translucent (~35% alpha) so player & solid trunk remain visible
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
                if (prop.HasCanopy &&
                    prop.WorldPositionX + 96f >= minX && prop.WorldPositionX - 96f <= maxX &&
                    prop.WorldPositionY + 96f >= minY && prop.WorldPositionY - 96f <= maxY)
                {
                    RenderPropCanopy(s_geometryBatch, prop, playerPos);
                }
            }
        }

        s_geometryBatch.End();

        // 5. Automated Screenshot Telemetry (captures frame 20 for analysis)
        s_frameCount++;
        if (s_frameCount >= 20 && !s_hasAutoCaptured)
        {
            s_hasAutoCaptured = true;
            CaptureScreen(window);
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

    private static void RenderPropGround(GeometryBatch batch, in MapProp prop)
    {
        Vector2 pos = prop.WorldPosition;
        ColorRgba primary = HexToColor(prop.ColorPrimary);
        ColorRgba secondary = HexToColor(prop.ColorSecondary);
        float r = prop.Radius;

        switch (prop.Type)
        {
            case MapPropType.OakTree:
                batch.FillCircle(new Vector2(pos.X + 4f, pos.Y + 14f), r * 0.45f, new ColorRgba(0, 0, 0, 45));
                batch.FillRectangle(new RectangleF(pos.X - 10f, pos.Y + 6f, 20f, 8f), new ColorRgba(69, 26, 3));
                batch.FillRectangle(new RectangleF(pos.X - 7f, pos.Y - 10f, 14f, 26f), secondary);
                batch.FillRectangle(new RectangleF(pos.X - 6f, pos.Y - 10f, 3f, 22f), new ColorRgba(146, 64, 14));
                break;

            case MapPropType.BirchTree:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 10f), r * 0.4f, new ColorRgba(0, 0, 0, 45));
                batch.FillRectangle(new RectangleF(pos.X - 5f, pos.Y - 8f, 10f, 30f), secondary);
                batch.FillRectangle(new RectangleF(pos.X - 5f, pos.Y + 2f, 4f, 2.5f), new ColorRgba(30, 41, 59));
                batch.FillRectangle(new RectangleF(pos.X + 1f, pos.Y + 10f, 4f, 2.5f), new ColorRgba(30, 41, 59));
                break;

            case MapPropType.PineTree:
            case MapPropType.SnowPine:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.35f, new ColorRgba(0, 0, 0, 45));
                batch.FillRectangle(new RectangleF(pos.X - 5f, pos.Y - 4f, 10f, 24f), new ColorRgba(113, 63, 18));
                break;

            case MapPropType.PalmTree:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.35f, new ColorRgba(0, 0, 0, 40));
                batch.FillRectangle(new RectangleF(pos.X - 4f, pos.Y - 6f, 8f, 32f), secondary);
                break;

            case MapPropType.WillowTree:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 12f), r * 0.45f, new ColorRgba(0, 0, 0, 50));
                batch.FillRectangle(new RectangleF(pos.X - 8f, pos.Y - 6f, 16f, 26f), secondary);
                break;

            case MapPropType.DeadTree:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.3f, new ColorRgba(0, 0, 0, 45));
                batch.FillRectangle(new RectangleF(pos.X - 5f, pos.Y - 14f, 10f, 32f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 18f, pos.Y - 8f, 16f, 5f), secondary);
                batch.FillRectangle(new RectangleF(pos.X + 2f, pos.Y - 16f, 18f, 5f), secondary);
                break;

            case MapPropType.Cactus:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 6f), r * 0.35f, new ColorRgba(0, 0, 0, 40));
                batch.FillRectangle(new RectangleF(pos.X - 6f, pos.Y - 20f, 12f, 40f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 18f, pos.Y - 8f, 14f, 6f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 18f, pos.Y - 22f, 6f, 16f), secondary);
                batch.FillRectangle(new RectangleF(pos.X + 4f, pos.Y - 4f, 14f, 6f), primary);
                batch.FillRectangle(new RectangleF(pos.X + 12f, pos.Y - 18f, 6f, 16f), secondary);
                break;

            case MapPropType.Bush:
                batch.FillCircle(new Vector2(pos.X - (r * 0.45f), pos.Y + 2f), r * 0.8f, primary);
                batch.FillCircle(new Vector2(pos.X + (r * 0.45f), pos.Y + 2f), r * 0.8f, primary);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 4f), r, secondary);
                break;

            case MapPropType.MushroomCluster:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 6f), r * 0.5f, new ColorRgba(0, 0, 0, 35));
                batch.FillRectangle(new RectangleF(pos.X - 8f, pos.Y + 2f, 4f, 9f), secondary);
                batch.FillRectangle(new RectangleF(pos.X + 4f, pos.Y + 4f, 3f, 7f), secondary);
                batch.FillCircle(new Vector2(pos.X - 6f, pos.Y - 2f), r * 0.75f, primary);
                batch.FillCircle(new Vector2(pos.X + 6f, pos.Y + 1f), r * 0.55f, primary);
                break;

            case MapPropType.CopperVein:
            case MapPropType.IronVein:
            case MapPropType.GoldVein:
            case MapPropType.CoalDeposit:
            case MapPropType.SilverVein:
                batch.FillCircle(new Vector2(pos.X + 4f, pos.Y + 8f), r * 0.85f, new ColorRgba(0, 0, 0, 50), segments: 8);
                batch.FillCircle(pos, r, primary, segments: 8);
                batch.FillCircle(new Vector2(pos.X + 3f, pos.Y + 3f), r * 0.7f, new ColorRgba(0, 0, 0, 55), segments: 8);
                batch.FillCircle(new Vector2(pos.X - 4f, pos.Y - 4f), r * 0.55f, new ColorRgba(255, 255, 255, 30), segments: 6);
                batch.FillCircle(new Vector2(pos.X - 5f, pos.Y - 4f), r * 0.28f, secondary, segments: 6);
                batch.FillCircle(new Vector2(pos.X + 5f, pos.Y + 3f), r * 0.25f, secondary, segments: 6);
                batch.FillCircle(new Vector2(pos.X + 2f, pos.Y - 6f), r * 0.22f, secondary, segments: 5);
                batch.FillCircle(new Vector2(pos.X - 2f, pos.Y + 6f), r * 0.20f, secondary, segments: 5);
                batch.FillCircle(new Vector2(pos.X - 6f, pos.Y - 5f), r * 0.12f, new ColorRgba(255, 255, 255, 220), segments: 4);
                break;

            case MapPropType.CrystalCluster:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 6f), r * 0.8f, new ColorRgba(0, 0, 0, 60), segments: 6);
                batch.FillCircle(pos, r * 0.85f, primary, segments: 6);
                batch.FillCircle(new Vector2(pos.X - 6f, pos.Y - 6f), r * 0.42f, secondary, segments: 5);
                batch.FillCircle(new Vector2(pos.X + 6f, pos.Y + 4f), r * 0.38f, secondary, segments: 5);
                batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.32f, new ColorRgba(255, 255, 255, 230), segments: 4);
                batch.FillCircle(new Vector2(pos.X + 3f, pos.Y - 8f), r * 0.35f, secondary, segments: 5);
                break;

            case MapPropType.Boulder:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 6f), r * 0.85f, new ColorRgba(0, 0, 0, 50), segments: 8);
                batch.FillCircle(pos, r, primary, segments: 8);
                batch.FillCircle(new Vector2(pos.X - 4f, pos.Y - 4f), r * 0.5f, secondary, segments: 8);
                break;

            case MapPropType.BasaltPillar:
                batch.FillCircle(new Vector2(pos.X, pos.Y + 8f), r * 0.85f, new ColorRgba(0, 0, 0, 60), segments: 6);
                batch.FillCircle(pos, r, primary, segments: 6);
                batch.FillCircle(pos, r * 0.65f, secondary, segments: 6);
                break;

            case MapPropType.AncientShrine:
                batch.FillCircle(pos, r + 12f, new ColorRgba(129, 140, 248, 65));
                batch.FillRectangle(new RectangleF(pos.X - 22f, pos.Y + 4f, 44f, 16f), secondary);
                batch.FillRectangle(new RectangleF(pos.X - 12f, pos.Y - 28f, 24f, 40f), secondary);
                batch.FillRectangle(new RectangleF(pos.X - 6f, pos.Y - 20f, 12f, 24f), primary);
                break;

            case MapPropType.None:
            default:
                break;
        }
    }

    private static void RenderPropCanopy(GeometryBatch batch, in MapProp prop, Vector2 playerPosition)
    {
        Vector2 pos = prop.WorldPosition;
        ColorRgba primary = HexToColor(prop.ColorPrimary);
        ColorRgba secondary = HexToColor(prop.ColorSecondary);
        float r = prop.Radius;

        // When player is standing under the tree canopy, leaves fade to ~35% alpha (90/255) so player and solid trunk remain visible
        byte alpha = prop.IsUnderCanopy(playerPosition) ? (byte)90 : (byte)255;
        byte subAlpha = (byte)(alpha * 180 / 255);

        switch (prop.Type)
        {
            case MapPropType.OakTree:
                batch.FillCircle(new Vector2(pos.X, pos.Y - 18f), r * 0.75f, new ColorRgba(15, 61, 30, subAlpha));
                batch.FillCircle(new Vector2(pos.X, pos.Y - 26f), r, new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillCircle(new Vector2(pos.X - (r * 0.45f), pos.Y - 20f), r * 0.72f, new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillCircle(new Vector2(pos.X + (r * 0.45f), pos.Y - 22f), r * 0.75f, new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillCircle(new Vector2(pos.X, pos.Y - 40f), r * 0.65f, new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillCircle(new Vector2(pos.X - 10f, pos.Y - 32f), r * 0.48f, new ColorRgba(34, 197, 94, alpha));
                batch.FillCircle(new Vector2(pos.X - 14f, pos.Y - 36f), r * 0.26f, new ColorRgba(74, 222, 128, alpha));
                break;

            case MapPropType.BirchTree:
                batch.FillCircle(new Vector2(pos.X, pos.Y - 26f), r, new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillCircle(new Vector2(pos.X - (r * 0.38f), pos.Y - 20f), r * 0.65f, new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillCircle(new Vector2(pos.X + (r * 0.38f), pos.Y - 22f), r * 0.65f, new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillCircle(new Vector2(pos.X - 4f, pos.Y - 30f), r * 0.45f, new ColorRgba(190, 242, 100, alpha));
                break;

            case MapPropType.PineTree:
            case MapPropType.SnowPine:
                batch.FillCircle(new Vector2(pos.X, pos.Y - 6f), r, new ColorRgba(secondary.R, secondary.G, secondary.B, alpha), segments: 16);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 22f), r * 0.75f, new ColorRgba(primary.R, primary.G, primary.B, alpha), segments: 16);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 36f), r * 0.48f, new ColorRgba(primary.R, primary.G, primary.B, alpha), segments: 16);
                break;

            case MapPropType.PalmTree:
                batch.FillCircle(new Vector2(pos.X, pos.Y - 26f), r, new ColorRgba(primary.R, primary.G, primary.B, alpha), segments: 14);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 26f), r * 0.45f, new ColorRgba(255, 255, 255, (byte)(75 * alpha / 255)));
                break;

            case MapPropType.WillowTree:
                batch.FillCircle(new Vector2(pos.X, pos.Y - 20f), r, new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillCircle(new Vector2(pos.X - (r * 0.45f), pos.Y - 4f), r * 0.45f, new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillCircle(new Vector2(pos.X + (r * 0.45f), pos.Y - 4f), r * 0.45f, new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillRectangle(new RectangleF(pos.X - 16f, pos.Y - 4f, 4f, 18f), new ColorRgba(primary.R, primary.G, primary.B, alpha));
                batch.FillRectangle(new RectangleF(pos.X + 12f, pos.Y - 4f, 4f, 18f), new ColorRgba(primary.R, primary.G, primary.B, alpha));
                break;
            case MapPropType.None:
                break;
            case MapPropType.DeadTree:
                break;
            case MapPropType.Cactus:
                break;
            case MapPropType.Bush:
                break;
            case MapPropType.MushroomCluster:
                break;
            case MapPropType.CopperVein:
                break;
            case MapPropType.IronVein:
                break;
            case MapPropType.GoldVein:
                break;
            case MapPropType.CoalDeposit:
                break;
            case MapPropType.SilverVein:
                break;
            case MapPropType.CrystalCluster:
                break;
            case MapPropType.Boulder:
                break;
            case MapPropType.BasaltPillar:
                break;
            case MapPropType.AncientShrine:
                break;
            default:
                break;
        }
    }

    private static void OnResize(IGameWindow window, int width, int height)
    {
        if (s_camera != null && width > 0 && height > 0)
        {
            s_camera.ViewportSize = new Vector2(width, height);
        }
    }

    private static ColorRgba HexToColor(uint hex)
    {
        byte r = (byte)((hex >> 24) & 0xFF);
        byte g = (byte)((hex >> 16) & 0xFF);
        byte b = (byte)((hex >> 8) & 0xFF);
        byte a = (byte)(hex & 0xFF);
        return new ColorRgba(r, g, b, a);
    }
}
