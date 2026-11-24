// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
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
using Carbon.Maps.Random;
using Carbon.Physics.Obstacles;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Carbon.Desktop;

/// <summary>
/// Entry point for the Carbon native desktop client runner.
/// Demonstrates procedural multi-biome world generation, camera controls, player movement, and obstacle collision.
/// </summary>
public static class Program
{
    private const float TileSize = 32f;
    private const int MapDimension = 128; // 128x128 tiles = 4096x4096 units world

    private static GeometryBatch? s_geometryBatch;
    private static Camera2D? s_camera;
    private static TilemapData? s_tilemap;
    private static WorldSimulation? s_world;
    private static Entity s_player = Entity.Null;
    private static BiomeType s_lastPlayerBiome = (BiomeType)255;
    private static ulong s_currentSeed = 1337UL;
    private static bool s_debugOverlay = true;

    /// <summary>
    /// Application main entry point.
    /// </summary>
    public static void Main()
    {
        Console.WriteLine("========================================================");
        Console.WriteLine("    Carbon MMORPG World Viewer — 10 Biomes & Physics    ");
        Console.WriteLine("========================================================");
        Console.WriteLine(" [W, A, S, D] / Arrows : Di chuyen nhan vat");
        Console.WriteLine(" [Left Shift]          : Chay nhanh (Sprint 450 u/s)");
        Console.WriteLine(" [Q] / [E]             : Thu nho / Phong to camera (Zoom)");
        Console.WriteLine(" [R]                   : Tai sinh ban do voi Seed ngau nhien");
        Console.WriteLine(" [B]                   : Bat / Tat hien thi vat can va cham");
        Console.WriteLine(" [Escape]              : Thoat khoi ung dung");
        Console.WriteLine("========================================================");

        WindowSettings settings = new()
        {
            Title = "Carbon MMORPG World Viewer (Silk.NET OpenGL)",
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
            Zoom = 1.0f
        };

        GenerateWorld(s_currentSeed);
    }

    private static void GenerateWorld(ulong seed)
    {
        s_currentSeed = seed;
        Console.WriteLine($"[WorldGen] Khoi tao ban do {MapDimension}x{MapDimension} tiles (Seed: 0x{seed:X16})...");

        ProceduralWorldGenerator generator = new(seed);
        s_tilemap = generator.GenerateTilemap(MapDimension, MapDimension, frequency: 0.015f);
        TileObstacleGrid obstacleGrid = s_tilemap.ToObstacleGrid(TileSize);

        s_world = new WorldSimulation(tickRate: 50, cellSize: TileSize)
        {
            ObstacleGrid = obstacleGrid
        };

        // Find safe land spawn point
        Vector2 spawnPoint = s_tilemap.FindSpawnPoint(TileSize, BiomeType.LushPlains);
        s_player = s_world.SpawnEntity(spawnPoint, radius: 10f);
        s_world.Entities.SetVelocity(s_player, new Velocity2D(0f, 0f, 5f));

        s_camera!.Position = spawnPoint;

        // Spawn a herd of wandering creatures around the player
        DeterministicRandom rng = new(seed ^ 0x9999999999999999UL);
        for (int i = 0; i < 20; i++)
        {
            float offsetX = rng.NextFloat(-250f, 250f);
            float offsetY = rng.NextFloat(-250f, 250f);
            Vector2 npcPos = spawnPoint + new Vector2(offsetX, offsetY);

            int tx = (int)(npcPos.X / TileSize);
            int ty = (int)(npcPos.Y / TileSize);
            if (tx >= 0 && tx < MapDimension && ty >= 0 && ty < MapDimension && !s_tilemap.GetTile(tx, ty).IsSolid)
            {
                Entity npc = s_world.SpawnEntity(npcPos, radius: 8f);
                Vector2 wanderDir = new(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f));
                if (wanderDir.LengthSquared() > 0.001f)
                {
                    wanderDir = Vector2.Normalize(wanderDir);
                }

                s_world.Entities.SetVelocity(npc, new Velocity2D(wanderDir.X * 40f, wanderDir.Y * 40f, 1f));
            }
        }

        Console.WriteLine($"[WorldGen] Nhan vat xuat hien tai toa do the gioi: ({spawnPoint.X:F1}, {spawnPoint.Y:F1})");
    }

    private static void OnUpdate(IGameWindow window, double dt)
    {
        if (s_world == null || s_tilemap == null || s_camera == null || !s_world.Entities.IsAlive(s_player))
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

        // 4. Zoom controls
        if (window.Input.Keyboard.IsKeyDown(Key.Q))
        {
            s_camera.Zoom = Math.Clamp(s_camera.Zoom * 0.985f, 0.25f, 3.0f);
        }
        else if (window.Input.Keyboard.IsKeyDown(Key.E))
        {
            s_camera.Zoom = Math.Clamp(s_camera.Zoom * 1.015f, 0.25f, 3.0f);
        }

        // 5. Movement controls
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
        int ptx = Math.Clamp((int)(playerTrans.Position.X / TileSize), 0, MapDimension - 1);
        int pty = Math.Clamp((int)(playerTrans.Position.Y / TileSize), 0, MapDimension - 1);
        ref readonly MapTile currentTile = ref s_tilemap.GetTile(ptx, pty);

        float finalSpeed = baseSpeed * currentTile.MoveSpeedMultiplier;

        if (moveDir.LengthSquared() > 0.001f)
        {
            moveDir = Vector2.Normalize(moveDir);
            ref Velocity2D vel = ref s_world.Entities.GetVelocity(s_player);
            vel.Linear = moveDir * finalSpeed;
        }

        // 6. Report biome changes
        if (currentTile.Biome != s_lastPlayerBiome)
        {
            s_lastPlayerBiome = currentTile.Biome;
            Console.WriteLine($"[Biome] Buoc vao quan xa: {currentTile.Biome,-16} (Toc do di chuyen: {currentTile.MoveSpeedMultiplier * 100:F0}%)");
        }

        // 7. Step simulation
        _ = s_world.Update(dt);

        // 8. Follow player with camera
        ref readonly Transform2D updatedTrans = ref s_world.Entities.GetTransform(s_player);
        s_camera.Position = updatedTrans.Position;
    }

    private static void OnRender(IGameWindow window, double dt)
    {
        if (s_geometryBatch == null || s_camera == null || s_tilemap == null || s_world == null)
        {
            return;
        }

        window.Device.SetClearColor(new Vector4(0.04f, 0.05f, 0.08f, 1.0f));
        window.Device.Clear(ClearBufferMask.ColorBufferBit);

        // Calculate visible tile range in camera view
        float halfW = window.Width * 0.5f / s_camera.Zoom;
        float halfH = window.Height * 0.5f / s_camera.Zoom;

        int minTileX = Math.Clamp((int)((s_camera.Position.X - halfW) / TileSize) - 1, 0, MapDimension - 1);
        int maxTileX = Math.Clamp((int)((s_camera.Position.X + halfW) / TileSize) + 1, 0, MapDimension - 1);
        int minTileY = Math.Clamp((int)((s_camera.Position.Y - halfH) / TileSize) - 1, 0, MapDimension - 1);
        int maxTileY = Math.Clamp((int)((s_camera.Position.Y + halfH) / TileSize) + 1, 0, MapDimension - 1);

        s_geometryBatch.Begin(s_camera.ViewProjectionMatrix);

        // 1. Render Visible Tiles
        for (int ty = minTileY; ty <= maxTileY; ty++)
        {
            for (int tx = minTileX; tx <= maxTileX; tx++)
            {
                ref readonly MapTile tile = ref s_tilemap.GetTile(tx, ty);
                ColorRgba baseColor = HexToColor(tile.ColorRgba);

                RectangleF rect = new(tx * TileSize, ty * TileSize, TileSize, TileSize);
                s_geometryBatch.FillRectangle(rect, baseColor);

                // Highlight solid obstacle tiles with dark inner stroke
                if (s_debugOverlay && tile.IsSolid)
                {
                    RectangleF innerBorder = new(
                        (tx * TileSize) + 2f,
                        (ty * TileSize) + 2f,
                        TileSize - 4f,
                        TileSize - 4f);
                    s_geometryBatch.FillRectangle(innerBorder, new ColorRgba(0, 0, 0, 80));
                }
            }
        }

        // 2. Render Active Entities
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
                // Player: Glowing cyan circle with white center and outline
                s_geometryBatch.FillCircle(trans.Position, col.Radius + 2f, new ColorRgba(56, 189, 248, 120));
                s_geometryBatch.FillCircle(trans.Position, col.Radius, new ColorRgba(255, 255, 255, 255));
                s_geometryBatch.FillCircle(trans.Position, col.Radius * 0.5f, new ColorRgba(14, 165, 233, 255));
            }
            else
            {
                // NPCs/Monsters: Crimson circle
                s_geometryBatch.FillCircle(trans.Position, col.Radius, new ColorRgba(239, 68, 68, 220));
                s_geometryBatch.FillCircle(trans.Position, col.Radius * 0.4f, new ColorRgba(185, 28, 28, 255));
            }
        }

        s_geometryBatch.End();

        GLEnum glError = window.Device.Gl.GetError();
        if (glError != GLEnum.NoError)
        {
            Console.WriteLine($"[OpenGL Error] {glError}");
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
