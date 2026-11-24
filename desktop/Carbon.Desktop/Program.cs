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
using Carbon.Maps.Props;
using Carbon.Maps.Random;
using Carbon.Maps.Streaming;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Components;
using Carbon.Simulation.World;
using Silk.NET.Input;
using Silk.NET.OpenGL;

namespace Carbon.Desktop;

/// <summary>
/// Entry point for the Carbon native desktop client runner.
/// Demonstrates infinite procedural chunk streaming, macro-continental biomes,
/// starter village settlement, and physical prop collision in an open-world MMORPG setting.
/// </summary>
public static class Program
{
    private const float TileSize = 32f;
    private const float MacroFrequency = 0.0025f; // Macro-continental scale (500+ tiles per biome)

    private static GeometryBatch? s_geometryBatch;
    private static Camera2D? s_camera;
    private static ChunkWorld2D? s_chunkWorld;
    private static WorldSimulation? s_world;
    private static Entity s_player = Entity.Null;
    private static BiomeType s_lastPlayerBiome = (BiomeType)255;
    private static (int X, int Y) s_lastChunkCoord = (int.MinValue, int.MinValue);
    private static ulong s_currentSeed = 1337UL;
    private static bool s_debugOverlay = true;

    /// <summary>
    /// Application main entry point.
    /// </summary>
    public static void Main()
    {
        Console.WriteLine("======================================================================");
        Console.WriteLine("   Carbon MMORPG Infinite World Viewer — Macro Biomes, Rivers & Town  ");
        Console.WriteLine("======================================================================");
        Console.WriteLine(" [W, A, S, D] / Arrows : Di chuyen nhan vat khap the gioi vo han");
        Console.WriteLine(" [Left Shift]          : Chay nhanh (Sprint 450 u/s)");
        Console.WriteLine(" [Q] / [E]             : Thu nho / Phong to camera (Zoom)");
        Console.WriteLine(" [R]                   : Tai sinh the gioi voi Seed ngau nhien moi");
        Console.WriteLine(" [B]                   : Bat / Tat hien thi vien va cham (Collision Overlay)");
        Console.WriteLine(" [Escape]              : Thoat khoi ung dung");
        Console.WriteLine("======================================================================");

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

        // Preload chunks around the safe starter spawn
        Vector2 spawnPoint = s_chunkWorld.SpawnWorldPosition;
        s_chunkWorld.UpdateAroundPosition(spawnPoint, chunkRadius: 4, evictionRadius: 8);

        s_world = new WorldSimulation(tickRate: 50, cellSize: TileSize);

        s_player = s_world.SpawnEntity(spawnPoint, radius: 10f);
        s_world.Entities.SetVelocity(s_player, new Velocity2D(0f, 0f, 5f));

        s_camera!.Position = spawnPoint;

        // Spawn a herd of roaming wildlife and village guards
        DeterministicRandom rng = new(seed ^ 0x9999999999999999UL);
        for (int i = 0; i < 25; i++)
        {
            float offsetX = rng.NextFloat(-350f, 350f);
            float offsetY = rng.NextFloat(-350f, 350f);
            Vector2 npcPos = spawnPoint + new Vector2(offsetX, offsetY);

            int tx = (int)(npcPos.X / TileSize);
            int ty = (int)(npcPos.Y / TileSize);
            MapTile tile = s_chunkWorld.GetTileAtWorld(tx, ty);

            if (!tile.IsSolid)
            {
                Entity npc = s_world.SpawnEntity(npcPos, radius: 8f);
                Vector2 wanderDir = new(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f));
                if (wanderDir.LengthSquared() > 0.001f)
                {
                    wanderDir = Vector2.Normalize(wanderDir);
                }

                s_world.Entities.SetVelocity(npc, new Velocity2D(wanderDir.X * 45f, wanderDir.Y * 45f, 1f));
            }
        }

        Console.WriteLine($"[WorldGen] Lang Khoi Dau thiet lap tai: ({spawnPoint.X:F1}, {spawnPoint.Y:F1})");
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

        // 4. Zoom controls
        if (window.Input.Keyboard.IsKeyDown(Key.Q))
        {
            s_camera.Zoom = Math.Clamp(s_camera.Zoom * 0.985f, 0.15f, 3.0f);
        }
        else if (window.Input.Keyboard.IsKeyDown(Key.E))
        {
            s_camera.Zoom = Math.Clamp(s_camera.Zoom * 1.015f, 0.15f, 3.0f);
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

        // 6. Report biome & chunk transitions
        if (currentTile.Biome != s_lastPlayerBiome)
        {
            s_lastPlayerBiome = currentTile.Biome;
            Console.WriteLine($"[Biome] Buoc vao quan xa: {currentTile.Biome,-16} (Toc do di chuyen: {currentTile.MoveSpeedMultiplier * 100:F0}%)");
        }

        int currentChunkX = (int)MathF.Floor(playerTrans.Position.X / (Chunk2D.Size * TileSize));
        int currentChunkY = (int)MathF.Floor(playerTrans.Position.Y / (Chunk2D.Size * TileSize));
        if (currentChunkX != s_lastChunkCoord.X || currentChunkY != s_lastChunkCoord.Y)
        {
            s_lastChunkCoord = (currentChunkX, currentChunkY);
            Console.WriteLine($"[ChunkStream] Di chuyen vao Chunk ({currentChunkX}, {currentChunkY}) | Active Chunks trong bo nho: {s_chunkWorld.LoadedChunkCount}");
        }

        // 7. Stream infinite chunks around the moving player
        s_chunkWorld.UpdateAroundPosition(playerTrans.Position, chunkRadius: 3, evictionRadius: 6);

        // 8. Step simulation
        _ = s_world.Update(dt);

        // 9. Follow player with camera
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

        float chunkSizeWorld = Chunk2D.Size * TileSize;
        int minChunkX = (int)MathF.Floor(minX / chunkSizeWorld) - 1;
        int maxChunkX = (int)MathF.Floor(maxX / chunkSizeWorld) + 1;
        int minChunkY = (int)MathF.Floor(minY / chunkSizeWorld) - 1;
        int maxChunkY = (int)MathF.Floor(maxY / chunkSizeWorld) + 1;

        s_geometryBatch.Begin(s_camera.ViewProjectionMatrix);

        // 1. Render Visible Chunks Terrain
        for (int cy = minChunkY; cy <= maxChunkY; cy++)
        {
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                if (!s_chunkWorld.TryGetChunk(cx, cy, out Chunk2D? chunk) || chunk == null)
                {
                    continue;
                }

                float baseChunkWorldX = cx * chunkSizeWorld;
                float baseChunkWorldY = cy * chunkSizeWorld;

                for (int ly = 0; ly < Chunk2D.Size; ly++)
                {
                    float tileWorldY = baseChunkWorldY + (ly * TileSize);
                    if (tileWorldY + TileSize < minY || tileWorldY > maxY)
                    {
                        continue;
                    }

                    for (int lx = 0; lx < Chunk2D.Size; lx++)
                    {
                        float tileWorldX = baseChunkWorldX + (lx * TileSize);
                        if (tileWorldX + TileSize < minX || tileWorldX > maxX)
                        {
                            continue;
                        }

                        ref readonly MapTile tile = ref chunk.GetTile(lx, ly);
                        ColorRgba baseColor = HexToColor(tile.ColorRgba);

                        // Subtle procedural variant shade for organic natural look
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

                        // Highlight solid terrain boundary in debug mode
                        if (s_debugOverlay && tile.IsSolid)
                        {
                            RectangleF innerBorder = new(tileWorldX + 2f, tileWorldY + 2f, TileSize - 4f, TileSize - 4f);
                            s_geometryBatch.FillRectangle(innerBorder, new ColorRgba(0, 0, 0, 70));
                        }
                    }
                }
            }
        }

        // 2. Render Props & Settlements
        for (int cy = minChunkY; cy <= maxChunkY; cy++)
        {
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                if (!s_chunkWorld.TryGetChunk(cx, cy, out Chunk2D? chunk) || chunk == null)
                {
                    continue;
                }

                for (int i = 0; i < chunk.Props.Count; i++)
                {
                    MapProp prop = chunk.Props[i];
                    if (prop.WorldPositionX + 24f >= minX && prop.WorldPositionX - 24f <= maxX &&
                        prop.WorldPositionY + 24f >= minY && prop.WorldPositionY - 24f <= maxY)
                    {
                        RenderProp(s_geometryBatch, prop);
                    }
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
                // Player: Glowing cyan circle with white center and outline
                s_geometryBatch.FillCircle(trans.Position, col.Radius + 3f, new ColorRgba(56, 189, 248, 120));
                s_geometryBatch.FillCircle(trans.Position, col.Radius, new ColorRgba(255, 255, 255, 255));
                s_geometryBatch.FillCircle(trans.Position, col.Radius * 0.5f, new ColorRgba(14, 165, 233, 255));
            }
            else
            {
                // Roaming creatures: Crimson circle with inner core
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

    private static void RenderProp(GeometryBatch batch, in MapProp prop)
    {
        Vector2 pos = prop.WorldPosition;
        ColorRgba primary = HexToColor(prop.ColorPrimary);
        ColorRgba secondary = HexToColor(prop.ColorSecondary);

        switch (prop.Type)
        {
            case MapPropType.OakTree:
                // Trunk + full rounded green crown
                batch.FillRectangle(new RectangleF(pos.X - 3.5f, pos.Y - 2f, 7f, 13f), secondary);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 8f), prop.Radius, primary);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 8f), prop.Radius * 0.55f, secondary);
                break;

            case MapPropType.PineTree:
            case MapPropType.SnowPine:
                // Trunk + conical needle layers
                batch.FillRectangle(new RectangleF(pos.X - 2.5f, pos.Y - 1f, 5f, 11f), new ColorRgba(113, 63, 18));
                batch.FillCircle(new Vector2(pos.X, pos.Y - 6f), prop.Radius, primary, segments: 16);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 11f), prop.Radius * 0.65f, secondary, segments: 16);
                break;

            case MapPropType.PalmTree:
                // Slender curved trunk + tropical palm fronds
                batch.FillRectangle(new RectangleF(pos.X - 2f, pos.Y - 2f, 4f, 14f), secondary);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 9f), prop.Radius, primary, segments: 12);
                batch.FillCircle(new Vector2(pos.X, pos.Y - 9f), prop.Radius * 0.45f, new ColorRgba(255, 255, 255, 80));
                break;

            case MapPropType.Cactus:
                // Saguaro cactus: central pillar with arms
                batch.FillRectangle(new RectangleF(pos.X - 3f, pos.Y - 11f, 6f, 22f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 9f, pos.Y - 7f, 18f, 4f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 9f, pos.Y - 13f, 4f, 6f), secondary);
                batch.FillRectangle(new RectangleF(pos.X + 5f, pos.Y - 13f, 4f, 6f), secondary);
                break;

            case MapPropType.DeadTree:
                // Gnarled dark trunk and branches
                batch.FillRectangle(new RectangleF(pos.X - 3f, pos.Y - 10f, 6f, 20f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 7f, pos.Y - 6f, 14f, 3.5f), secondary);
                break;

            case MapPropType.BasaltPillar:
                // Hexagonal volcanic basalt column
                batch.FillCircle(pos, prop.Radius, primary, segments: 6);
                batch.FillCircle(pos, prop.Radius * 0.65f, secondary, segments: 6);
                break;

            case MapPropType.Boulder:
                // Natural smoothed rock
                batch.FillCircle(pos, prop.Radius, primary, segments: 8);
                batch.FillCircle(new Vector2(pos.X - 2f, pos.Y - 2f), prop.Radius * 0.5f, secondary, segments: 8);
                break;

            case MapPropType.TownWell:
                // Central Village Well: stone base + dark water + wooden roof cross beam
                batch.FillRectangle(new RectangleF(pos.X - 10f, pos.Y - 10f, 20f, 20f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 6f, pos.Y - 6f, 12f, 12f), new ColorRgba(14, 165, 233));
                batch.FillRectangle(new RectangleF(pos.X - 11f, pos.Y - 2.5f, 22f, 5f), secondary);
                break;

            case MapPropType.Campfire:
                // Warm glowing campfire with stone ring
                batch.FillCircle(pos, prop.Radius + 6f, new ColorRgba(249, 115, 22, 60));
                batch.FillCircle(pos, prop.Radius, secondary, segments: 8);
                batch.FillCircle(pos, prop.Radius * 0.5f, primary);
                break;

            case MapPropType.CobblestoneRoad:
                // Paved stone highway tile
                batch.FillRectangle(new RectangleF(pos.X - 15f, pos.Y - 15f, 30f, 30f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 13f, pos.Y - 13f, 11f, 11f), secondary);
                batch.FillRectangle(new RectangleF(pos.X + 2f, pos.Y + 2f, 11f, 11f), secondary);
                break;

            case MapPropType.WoodenFence:
                // Rustic wooden fence barrier
                batch.FillRectangle(new RectangleF(pos.X - 15f, pos.Y - 3f, 30f, 6f), primary);
                batch.FillRectangle(new RectangleF(pos.X - 4f, pos.Y - 7f, 8f, 14f), secondary);
                break;

            case MapPropType.AncientShrine:
                // Mysterious purple obelisk with glowing cyan aura
                batch.FillCircle(pos, prop.Radius + 7f, new ColorRgba(129, 140, 248, 70));
                batch.FillRectangle(new RectangleF(pos.X - 9f, pos.Y - 13f, 18f, 26f), secondary);
                batch.FillRectangle(new RectangleF(pos.X - 5f, pos.Y - 9f, 10f, 18f), primary);
                break;

            case MapPropType.WildflowerPatch:
                // Colorful flowers in meadows
                batch.FillCircle(new Vector2(pos.X - 4f, pos.Y - 4f), 3.5f, primary);
                batch.FillCircle(new Vector2(pos.X + 4f, pos.Y + 2f), 3.5f, secondary);
                batch.FillCircle(new Vector2(pos.X + 1f, pos.Y - 3f), 3f, new ColorRgba(255, 255, 255));
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
