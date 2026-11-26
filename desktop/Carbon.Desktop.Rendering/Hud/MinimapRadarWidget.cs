// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Contracts.Entities;
using Carbon.Desktop.Abstractions;
using Carbon.Graphics.Batching.Common;
using Carbon.Graphics.Batching.Geometry;
using Carbon.Graphics.Batching.Sprites;
using Carbon.Graphics.Fonts.Rendering;
using Carbon.Maps.Biomes;
using Carbon.Maps.Data;
using Carbon.Maps.Streaming;

namespace Carbon.Desktop.Rendering.Hud;

/// <summary>Top-right radar minimap widget rendering nearby terrain biomes, actors, and cardinal orientation (SRP).</summary>
internal sealed class MinimapRadarWidget
{
    public void DrawGeometry(GeometryBatch batch, IClientWorldReadModel world, int viewportWidth, int ptx, int pty)
    {
        const float mapSize = 156f;
        const float pad = 6f;
        float mapX = viewportWidth - mapSize - 20f;
        const float mapY = 40f;

        batch.FillRectangle(new RectangleF(mapX - pad, mapY - 26f, mapSize + (pad * 2f), mapSize + 32f), new ColorRgba(10, 15, 28, 224));
        batch.DrawRectangle(new RectangleF(mapX - pad, mapY - 26f, mapSize + (pad * 2f), mapSize + 32f), new ColorRgba(71, 85, 105, 230), 1.5f);

        const int radarRadius = 26;
        float cellSize = mapSize / (radarRadius * 2f);
        ChunkWorld2D chunkWorld = world.ChunkWorld;

        for (int dy = -radarRadius; dy < radarRadius; dy += 2)
        {
            for (int dx = -radarRadius; dx < radarRadius; dx += 2)
            {
                MapTile tile = chunkWorld.GetTileAtWorld(ptx + dx, pty + dy);
                ColorRgba color = ResolveMinimapBiomeColor(tile.Biome);
                float rx = mapX + ((dx + radarRadius) * cellSize);
                float ry = mapY + ((dy + radarRadius) * cellSize);
                batch.FillRectangle(new RectangleF(rx, ry, cellSize * 2f, cellSize * 2f), color);
            }
        }

        IReadOnlyList<RemoteEntityView> remotes = world.RemoteEntities;
        float tileSize = chunkWorld.TileSize;
        for (int i = 0; i < remotes.Count; i++)
        {
            RemoteEntityView r = remotes[i];
            int rdx = (int)MathF.Floor(r.Position.X / tileSize) - ptx;
            int rdy = (int)MathF.Floor(r.Position.Y / tileSize) - pty;
            if (Math.Abs(rdx) < radarRadius && Math.Abs(rdy) < radarRadius)
            {
                ColorRgba blip = r.Kind == EntityKind.Player
                    ? new ColorRgba(56, 189, 248, 255)
                    : r.Kind == EntityKind.Npc
                        ? new ColorRgba(250, 204, 21, 255)
                        : new ColorRgba(248, 113, 113, 255);
                float bx = mapX + ((rdx + radarRadius) * cellSize);
                float by = mapY + ((rdy + radarRadius) * cellSize);
                batch.FillCircle(new Vector2(bx, by), 2.5f, blip, 8);
            }
        }

        float centerPx = mapX + (mapSize * 0.5f);
        float centerPy = mapY + (mapSize * 0.5f);
        batch.FillCircle(new Vector2(centerPx, centerPy), 4f, ColorRgba.White, 10);
        batch.DrawCircle(new Vector2(centerPx, centerPy), 4f, new ColorRgba(15, 23, 42, 255), 10, 1.5f);
        batch.DrawRectangle(new RectangleF(mapX, mapY, mapSize, mapSize), new ColorRgba(100, 116, 139, 230), 1f);
    }

    public void DrawText(SpriteBatch spriteBatch, BitmapFont font, int viewportWidth, int ptx, int pty)
    {
        float mapX = MathF.Floor(viewportWidth - 176f);
        ColorRgba black = new(10, 12, 18, 255);
        spriteBatch.DrawOutlinedString(font, $"MAP ({ptx},{pty})", new Vector2(mapX, 20f), new ColorRgba(248, 250, 252, 255), black, 1, 2f);
        spriteBatch.DrawOutlinedString(font, "N", new Vector2(mapX + 75f, 43f), new ColorRgba(250, 204, 21, 235), black, 1, 1f);
    }

    private static ColorRgba ResolveMinimapBiomeColor(BiomeType biome)
    {
        if (biome == BiomeType.DeepOcean)
        {
            return new ColorRgba(17, 45, 92, 255);
        }

        if (biome == BiomeType.ShallowWater)
        {
            return new ColorRgba(38, 109, 168, 255);
        }

        if (biome == BiomeType.BeachSand)
        {
            return new ColorRgba(225, 200, 140, 255);
        }

        if (biome == BiomeType.AncientForest)
        {
            return new ColorRgba(36, 98, 52, 255);
        }

        if (biome == BiomeType.MistySwamp)
        {
            return new ColorRgba(62, 92, 74, 255);
        }

        if (biome == BiomeType.DuneDesert || biome == BiomeType.Savanna)
        {
            return new ColorRgba(209, 174, 96, 255);
        }

        if (biome == BiomeType.RedMesa)
        {
            return new ColorRgba(176, 86, 54, 255);
        }

        if (biome == BiomeType.TropicalJungle)
        {
            return new ColorRgba(28, 124, 62, 255);
        }

        if (biome == BiomeType.FrozenTundra || biome == BiomeType.AlpinePeaks)
        {
            return new ColorRgba(214, 228, 240, 255);
        }

        if (biome == BiomeType.VolcanicAshland)
        {
            return new ColorRgba(78, 66, 68, 255);
        }

        if (biome == BiomeType.BlossomWoods)
        {
            return new ColorRgba(196, 114, 158, 255);
        }

        return new ColorRgba(82, 152, 78, 255);
    }
}
