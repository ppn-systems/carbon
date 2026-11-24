// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.IO;
using System.Numerics;
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
    private static Camera2D? s_camera;
    private static ChunkWorld2D? s_chunkWorld;
    private static WorldSimulation? s_world;
    private static Entity s_player = Entity.Null;
    private static BiomeType s_lastPlayerBiome = (BiomeType)255;
    private static (int X, int Y) s_lastChunkCoord = (int.MinValue, int.MinValue);
    private static ulong s_currentSeed = 1337UL;
    private static bool s_debugOverlay = true;
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

        window.Run();
    }

    private static void OnLoad(IGameWindow window)
    {
        Console.WriteLine($"[OpenGL] Vendor:   {window.Device.Gl.GetStringS(StringName.Vendor)}");
        Console.WriteLine($"[OpenGL] Renderer: {window.Device.Gl.GetStringS(StringName.Renderer)}");
        Console.WriteLine($"[OpenGL] Version:  {window.Device.Gl.GetStringS(StringName.Version)}");

        s_geometryBatch = new GeometryBatch(window.Device);
        s_camera = new Camera2D(window.Width, window.Height)
        {
            Zoom = 0.8f
        };

        GenerateWorld(s_currentSeed);
    }

    private static void GenerateWorld(ulong seed)
    {
        s_currentSeed = seed;
        Console.WriteLine($"[WorldGen] Khoi tao the gioi vo han (Seed: 0x{seed:X16}, MacroFreq: {MacroFrequency:F4})...");

        ProceduralWorldGenerator generator = new(seed);
        s_chunkWorld = new ChunkWorld2D(generator, TileSize, MacroFrequency);

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
        }

        // 8. Stream infinite chunks around the moving player with pooling
        s_chunkWorld.UpdateAroundPosition(playerTrans.Position, chunkRadius: 4, evictionRadius: 7);

        // 9. Step simulation
        _ = s_world.Update(dt);

        // 10. Follow player with camera
        ref readonly Transform2D updatedTrans = ref s_world.Entities.GetTransform(s_player);
        s_camera.Position = updatedTrans.Position;
    }

    private static void OnRender(IGameWindow window, double dt)
    {
        if (s_geometryBatch == null || s_camera == null || s_chunkWorld == null || s_world == null)
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

        s_geometryBatch.Begin(s_camera.ViewProjectionMatrix);

        // 1. Render Visible Chunks Terrain with AABB Frustum Culling
        foreach (Chunk2D chunk in s_chunkWorld.ActiveChunks)
        {
            chunk.GetBounds(TileSize, out float cMinX, out float cMinY, out float cMaxX, out float cMaxY);

            // Frustum Culling AABB test: skip entire chunk if outside camera view
            if (cMaxX < minX || cMinX > maxX || cMaxY < minY || cMinY > maxY)
            {
                continue;
            }

            for (int ly = 0; ly < Chunk2D.Size; ly++)
            {
                float tileWorldY = cMinY + (ly * TileSize);
                if (tileWorldY + TileSize < minY || tileWorldY > maxY)
                {
                    continue;
                }

                for (int lx = 0; lx < Chunk2D.Size; lx++)
                {
                    float tileWorldX = cMinX + (lx * TileSize);
                    if (tileWorldX + TileSize < minX || tileWorldX > maxX)
                    {
                        continue;
                    }

                    ref readonly MapTile tile = ref chunk.GetTile(lx, ly);
                    ColorRgba baseColor = HexToColor(tile.ColorRgba);

                    // Organic procedural variant shading
                    if (tile.Variant != 0)
                    {
                        int shade = (tile.Variant * 6) - 9;
                        baseColor = new ColorRgba(
                            (byte)Math.Clamp(baseColor.R + shade, 0, 255),
                            (byte)Math.Clamp(baseColor.G + shade, 0, 255),
                            (byte)Math.Clamp(baseColor.B + shade, 0, 255),
                            baseColor.A);
                    }

                    RectangleF rect = new(tileWorldX, tileWorldY, TileSize, TileSize);
                    s_geometryBatch.FillRectangle(rect, baseColor);

                    // Highlight solid obstacles in debug mode
                    if (s_debugOverlay && tile.IsSolid)
                    {
                        RectangleF innerBorder = new(tileWorldX + 2f, tileWorldY + 2f, TileSize - 4f, TileSize - 4f);
                        s_geometryBatch.FillRectangle(innerBorder, new ColorRgba(0, 0, 0, 70));
                    }
                }
            }
        }

        // 2. Render Props with Frustum Culling
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
                if (prop.WorldPositionX + 32f >= minX && prop.WorldPositionX - 32f <= maxX &&
                    prop.WorldPositionY + 32f >= minY && prop.WorldPositionY - 32f <= maxY)
                {
                    RenderProp(s_geometryBatch, prop);
                }
            }
        }

        // 3. Render Active Entities
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
                // Player: Sleek MMORPG hero avatar token (drop shadow, navy tunic, cyan trim, white center dot)
                s_geometryBatch.FillCircle(new Vector2(trans.Position.X, trans.Position.Y + 2f), col.Radius + 1f, new ColorRgba(0, 0, 0, 80));
                s_geometryBatch.FillCircle(trans.Position, col.Radius, new ColorRgba(30, 58, 138, 255));
                s_geometryBatch.FillCircle(trans.Position, col.Radius * 0.65f, new ColorRgba(56, 189, 248, 255));
                s_geometryBatch.FillCircle(trans.Position, col.Radius * 0.3f, new ColorRgba(255, 255, 255, 255));
            }
        }

        s_geometryBatch.End();

        // 4. Automated Screenshot Telemetry (captures frame 60 for analysis)
        s_frameCount++;
        if (s_frameCount >= 60 && !s_hasAutoCaptured)
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

    private static void RenderProp(GeometryBatch batch, in MapProp prop)
    {
        Vector2 pos = prop.WorldPosition;
        ColorRgba primary = HexToColor(prop.ColorPrimary);
        ColorRgba secondary = HexToColor(prop.ColorSecondary);

        switch (prop.Type)
        {
            case MapPropType.OakTree:
                batch.FillRectangle(new RectangleF(pos.X - 4f, pos.Y - 2f, 8f, 16f), secondary);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 8f), prop.Radius, primary);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 8f), prop.Radius * 0.65f, secondary);
                break;

            case MapPropType.BirchTree:
                batch.FillRectangle(new RectangleF(pos.X - 3f, pos.Y - 2f, 6f, 16f), secondary);
                batch.FillRectangle(new RectangleF(pos.X - 3f, pos.Y + 3f, 3f, 2f), new ColorRgba(30, 41, 59));
                batch.FillRectangle(new RectangleF(pos.X, pos.Y + 8f, 3f, 2f), new ColorRgba(30, 41, 59));
                batch.FillCircle(new Vector2(pos.X, pos.Y - 8f), prop.Radius, primary);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 8f), prop.Radius * 0.6f, new ColorRgba(163, 230, 53));
                break;

            case MapPropType.PineTree:
            case MapPropType.SnowPine:
                batch.FillRectangle(new RectangleF(pos.X - 3f, pos.Y - 1f, 6f, 15f), new ColorRgba(113, 63, 18));
                batch.FillCircle(new Vector2(pos.X, pos.Y - 4f), prop.Radius, secondary, segments: 16);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 10f), prop.Radius * 0.75f, primary, segments: 16);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 15f), prop.Radius * 0.45f, primary, segments: 16);
                break;

            case MapPropType.PalmTree:
                batch.FillRectangle(new RectangleF(pos.X - 2.5f, pos.Y - 2f, 5f, 18f), secondary);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 10f), prop.Radius, primary, segments: 12);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 10f), prop.Radius * 0.45f, new ColorRgba(255, 255, 255, 80));
                break;

            case MapPropType.WillowTree:
                batch.FillRectangle(new RectangleF(pos.X - 4f, pos.Y - 2f, 8f, 15f), secondary);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 7f), prop.Radius, primary);
                batch.FillCircle(new Vector2(pos.X - 6f, pos.Y + 2f), prop.Radius * 0.4f, primary);
                batch.FillCircle(new Vector2(pos.X + 6f, pos.Y + 2f), prop.Radius * 0.4f, primary);
                break;

            case MapPropType.DeadTree:
                batch.FillRectangle(new RectangleF(pos.X - 3.5f, pos.Y - 10f, 7f, 22f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 8f, pos.Y - 6f, 16f, 3.5f), secondary);
                break;

            case MapPropType.Cactus:
                batch.FillRectangle(new RectangleF(pos.X - 3.5f, pos.Y - 12f, 7f, 24f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 10f, pos.Y - 7f, 20f, 4f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 10f, pos.Y - 14f, 4f, 7f), secondary);
                batch.FillRectangle(new RectangleF(pos.X + 6f, pos.Y - 14f, 4f, 7f), secondary);
                break;

            case MapPropType.Bush:
                batch.FillCircle(new Vector2(pos.X - 3f, pos.Y + 1f), prop.Radius * 0.75f, primary);
                batch.FillCircle(new Vector2(pos.X + 3f, pos.Y + 1f), prop.Radius * 0.75f, primary);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 2f), prop.Radius, secondary);
                break;

            case MapPropType.MushroomCluster:
                batch.FillRectangle(new RectangleF(pos.X - 4f, pos.Y + 1f, 2f, 5f), secondary);
                batch.FillRectangle(new RectangleF(pos.X + 2f, pos.Y + 2f, 2f, 4f), secondary);
                batch.FillCircle(new Vector2(pos.X - 3f, pos.Y), prop.Radius * 0.7f, primary);
                batch.FillCircle(new Vector2(pos.X + 3f, pos.Y + 1f), prop.Radius * 0.55f, primary);
                break;

            case MapPropType.CopperVein:
            case MapPropType.IronVein:
            case MapPropType.GoldVein:
            case MapPropType.CoalDeposit:
            case MapPropType.SilverVein:
                batch.FillCircle(pos, prop.Radius, primary, segments: 8);
                batch.FillCircle(new Vector2(pos.X - 2.5f, pos.Y - 2f), prop.Radius * 0.35f, secondary, segments: 6);
                batch.FillCircle(new Vector2(pos.X + 2.5f, pos.Y + 1.5f), prop.Radius * 0.3f, secondary, segments: 6);
                break;

            case MapPropType.CrystalCluster:
                batch.FillCircle(pos, prop.Radius * 0.7f, primary, segments: 6);
                batch.FillCircle(new Vector2(pos.X - 3f, pos.Y - 3f), prop.Radius * 0.45f, secondary, segments: 5);
                batch.FillCircle(new Vector2(pos.X + 3f, pos.Y + 2f), prop.Radius * 0.4f, secondary, segments: 5);
                batch.FillCircle(new Vector2(pos.X, pos.Y + 4f), prop.Radius * 0.35f, new ColorRgba(255, 255, 255, 220), segments: 4);
                break;

            case MapPropType.Boulder:
                batch.FillCircle(pos, prop.Radius, primary, segments: 8);
                batch.FillCircle(new Vector2(pos.X - 2f, pos.Y - 2f), prop.Radius * 0.5f, secondary, segments: 8);
                break;

            case MapPropType.BasaltPillar:
                batch.FillCircle(pos, prop.Radius, primary, segments: 6);
                batch.FillCircle(pos, prop.Radius * 0.65f, secondary, segments: 6);
                break;

            case MapPropType.AncientShrine:
                batch.FillCircle(pos, prop.Radius + 8f, new ColorRgba(129, 140, 248, 70));
                batch.FillRectangle(new RectangleF(pos.X - 9f, pos.Y - 13f, 18f, 26f), secondary);
                batch.FillRectangle(new RectangleF(pos.X - 5f, pos.Y - 9f, 10f, 18f), primary);
                break;

            case MapPropType.None:
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
