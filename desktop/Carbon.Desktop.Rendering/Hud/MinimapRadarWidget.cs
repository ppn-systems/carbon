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

/// <summary>
/// Top-right cached radar minimap widget rendering nearby terrain biomes, actors, locked target highlight,
/// static header, and clean cardinal orientation without per-frame terrain tile churn (SRP).
/// </summary>
internal sealed class MinimapRadarWidget
{
    private const int RadarRadius = 26;
    private const int GridSide = RadarRadius;
    private readonly ColorRgba[] _tileCache = new ColorRgba[GridSide * GridSide];
    private int _cachedPtx = int.MinValue;
    private int _cachedPty = int.MinValue;
    private ulong _cachedSeed;

    public void DrawGeometry(GeometryBatch batch, IClientWorldReadModel world, int viewportWidth, int ptx, int pty)
    {
        const float mapSize = 152f;
        const float pad = 8f;
        float mapX = viewportWidth - mapSize - 24f;
        const float mapY = 38f;

        batch.FillRectangle(new RectangleF(mapX - pad, mapY - 24f, mapSize + (pad * 2f), mapSize + 44f), new ColorRgba(10, 15, 28, 228));
        batch.DrawRectangle(new RectangleF(mapX - pad, mapY - 24f, mapSize + (pad * 2f), mapSize + 44f), new ColorRgba(71, 85, 105, 230), 1.5f);

        float cellSize = mapSize / (RadarRadius * 2f);
        ChunkWorld2D chunkWorld = world.ChunkWorld;

        if (_cachedPtx != ptx || _cachedPty != pty || _cachedSeed != world.WorldSeed)
        {
            _cachedPtx = ptx;
            _cachedPty = pty;
            _cachedSeed = world.WorldSeed;
            int idx = 0;
            for (int dy = -RadarRadius; dy < RadarRadius; dy += 2)
            {
                for (int dx = -RadarRadius; dx < RadarRadius; dx += 2)
                {
                    MapTile tile = chunkWorld.GetTileAtWorld(ptx + dx, pty + dy);
                    _tileCache[idx++] = ResolveMinimapBiomeColor(tile.Biome);
                }
            }
        }

        int cacheIdx = 0;
        for (int dy = -RadarRadius; dy < RadarRadius; dy += 2)
        {
            float ry = mapY + ((dy + RadarRadius) * cellSize);
            for (int dx = -RadarRadius; dx < RadarRadius; dx += 2)
            {
                float rx = mapX + ((dx + RadarRadius) * cellSize);
                batch.FillRectangle(new RectangleF(rx, ry, cellSize * 2f, cellSize * 2f), _tileCache[cacheIdx++]);
            }
        }

        IReadOnlyList<RemoteEntityView> remotes = world.RemoteEntities;
        float tileSize = chunkWorld.TileSize;
        for (int i = 0; i < remotes.Count; i++)
        {
            RemoteEntityView r = remotes[i];
            if (r.Kind == EntityKind.LootDrop)
            {
                continue;
            }

            int rdx = (int)MathF.Floor(r.Position.X / tileSize) - ptx;
            int rdy = (int)MathF.Floor(r.Position.Y / tileSize) - pty;
            if (Math.Abs(rdx) < RadarRadius && Math.Abs(rdy) < RadarRadius)
            {
                ColorRgba blip = r.Kind == EntityKind.Player
                    ? new ColorRgba(56, 189, 248, 255)
                    : r.Kind == EntityKind.Npc
                        ? new ColorRgba(250, 204, 21, 255)
                        : new ColorRgba(248, 113, 113, 255);
                float bx = mapX + ((rdx + RadarRadius) * cellSize);
                float by = mapY + ((rdy + RadarRadius) * cellSize);
                batch.FillCircle(new Vector2(bx, by), 2.5f, blip, 8);
                if (r.NetworkId == world.LockedTargetNetworkId)
                {
                    batch.DrawCircle(new Vector2(bx, by), 4.2f, new ColorRgba(250, 204, 21, 255), 10, 1.2f);
                }
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
        spriteBatch.DrawOutlinedString(font, "WORLD RADAR", new Vector2(mapX, 19f), new ColorRgba(248, 250, 252, 255), black, 1, 1.5f);
        spriteBatch.DrawOutlinedString(font, "N", new Vector2(mapX + 73f, 29f), new ColorRgba(250, 204, 21, 245), black, 1, 1f);
        spriteBatch.DrawOutlinedString(font, $"({ptx},{pty})", new Vector2(mapX, 194f), new ColorRgba(148, 163, 184, 255), black, 1, 1f);
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
