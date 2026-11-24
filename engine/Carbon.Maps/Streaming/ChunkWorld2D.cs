// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Props;
using Carbon.Physics.Obstacles;

namespace Carbon.Maps.Streaming;

/// <summary>
/// High-performance chunk-streaming world coordinator managing infinite procedural 2D terrain.
/// Employs Minecraft-style chunk lifecycle management with object pooling to guarantee zero GC allocations during exploration.
/// </summary>
public sealed class ChunkWorld2D
{
    private readonly ProceduralWorldGenerator _generator;
    private readonly float _tileSize;
    private readonly float _frequency;
    private readonly ConcurrentDictionary<long, Chunk2D> _activeChunks = new();
    private readonly ConcurrentQueue<Chunk2D> _chunkPool = new();
    private readonly List<long> _evictionBuffer = new();
    private readonly BuildingGrid _buildings = new();

    private readonly int _spawnTileX;
    private readonly int _spawnTileY;
    private readonly Vector2 _spawnWorldPosition;

    /// <summary>
    /// Gets the spatial building grid managing constructed structures, connected walls, and doors.
    /// </summary>
    public BuildingGrid Buildings => _buildings;

    /// <summary>
    /// Gets the deterministic generator powering world synthesis.
    /// </summary>
    public ProceduralWorldGenerator Generator => _generator;

    /// <summary>
    /// Gets the physical dimension of each tile in world units.
    /// </summary>
    public float TileSize => _tileSize;

    /// <summary>
    /// Gets the world spawn coordinate vector located on safe, natural starter land.
    /// </summary>
    public Vector2 SpawnWorldPosition => _spawnWorldPosition;

    /// <summary>
    /// Gets the world spawn coordinate in tile units.
    /// </summary>
    public (int X, int Y) SpawnTile => (_spawnTileX, _spawnTileY);

    /// <summary>
    /// Gets the collection of currently resident active chunks.
    /// </summary>
    public ICollection<Chunk2D> ActiveChunks => _activeChunks.Values;

    /// <summary>
    /// Gets the count of currently resident active chunks in memory.
    /// </summary>
    public int LoadedChunkCount => _activeChunks.Count;

    /// <summary>
    /// Gets the count of idle recycled chunks currently held in the object pool.
    /// </summary>
    public int PooledChunkCount => _chunkPool.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkWorld2D"/> class.
    /// Automatically locates a natural safe starter location within a grand LushPlains biome.
    /// </summary>
    /// <param name="generator">The procedural terrain generator.</param>
    /// <param name="tileSize">Physical dimension of each tile (default 32f).</param>
    /// <param name="frequency">Macro spatial noise frequency (default 0.0008f).</param>
    public ChunkWorld2D(ProceduralWorldGenerator generator, float tileSize = 32f, float frequency = ProceduralWorldGenerator.DefaultFrequency)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _tileSize = tileSize;
        _frequency = frequency;

        (int sx, int sy) = LocateSafeNaturalSpawn(generator, frequency);
        _spawnTileX = sx;
        _spawnTileY = sy;
        _spawnWorldPosition = new Vector2((sx * tileSize) + (tileSize * 0.5f), (sy * tileSize) + (tileSize * 0.5f));
    }

    /// <summary>
    /// Retrieves an existing chunk or generates one using object pooling to eliminate GC pressure.
    /// </summary>
    /// <param name="chunkX">Chunk X coordinate.</param>
    /// <param name="chunkY">Chunk Y coordinate.</param>
    /// <returns>Populated <see cref="Chunk2D"/>.</returns>
    public Chunk2D GetOrCreateChunk(int chunkX, int chunkY)
    {
        long key = PackChunkKey(chunkX, chunkY);
        if (_activeChunks.TryGetValue(key, out Chunk2D? existing))
        {
            return existing;
        }

        Chunk2D chunk = _chunkPool.TryDequeue(out Chunk2D? recycled) ? recycled : new Chunk2D(chunkX, chunkY);
        _generator.PopulateChunk(chunk, chunkX, chunkY, _frequency, _tileSize);

        _activeChunks[key] = chunk;
        return chunk;
    }

    /// <summary>
    /// Attempts to retrieve a loaded chunk without generating it if absent.
    /// </summary>
    public bool TryGetChunk(int chunkX, int chunkY, out Chunk2D? chunk)
    {
        long key = PackChunkKey(chunkX, chunkY);
        return _activeChunks.TryGetValue(key, out chunk);
    }

    /// <summary>
    /// Streams and loads all chunks within <paramref name="chunkRadius"/> around the target world position,
    /// recycling chunks farther than <paramref name="evictionRadius"/> back into the pool.
    /// </summary>
    /// <param name="centerWorldPos">Target center world coordinates (e.g. camera or player).</param>
    /// <param name="chunkRadius">Active load radius in chunks (e.g. 4 chunks = 9x9 active grid).</param>
    /// <param name="evictionRadius">Distance in chunks beyond which inactive chunks are recycled into the pool (e.g. 7 chunks).</param>
    public void UpdateAroundPosition(Vector2 centerWorldPos, int chunkRadius = 4, int evictionRadius = 7)
    {
        float chunkSizeWorld = Chunk2D.Size * _tileSize;
        int centerChunkX = (int)MathF.Floor(centerWorldPos.X / chunkSizeWorld);
        int centerChunkY = (int)MathF.Floor(centerWorldPos.Y / chunkSizeWorld);

        // 1. Ensure all chunks within load radius are resident
        for (int cy = centerChunkY - chunkRadius; cy <= centerChunkY + chunkRadius; cy++)
        {
            for (int cx = centerChunkX - chunkRadius; cx <= centerChunkX + chunkRadius; cx++)
            {
                _ = this.GetOrCreateChunk(cx, cy);
            }
        }

        // 2. Recycle distant chunks beyond eviction radius
        _evictionBuffer.Clear();
        foreach (KeyValuePair<long, Chunk2D> kvp in _activeChunks)
        {
            (int kx, int ky) = UnpackChunkKey(kvp.Key);
            int dist = Math.Max(Math.Abs(kx - centerChunkX), Math.Abs(ky - centerChunkY));
            if (dist > evictionRadius)
            {
                _evictionBuffer.Add(kvp.Key);
            }
        }

        for (int i = 0; i < _evictionBuffer.Count; i++)
        {
            if (_activeChunks.TryRemove(_evictionBuffer[i], out Chunk2D? evicted))
            {
                _chunkPool.Enqueue(evicted);
            }
        }
    }

    /// <summary>
    /// Evaluates the terrain tile at an arbitrary world tile coordinate.
    /// </summary>
    public MapTile GetTileAtWorld(int worldTileX, int worldTileY)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        int lx = Chunk2D.ToLocalCoord(worldTileX);
        int ly = Chunk2D.ToLocalCoord(worldTileY);

        Chunk2D chunk = this.GetOrCreateChunk(cx, cy);
        return chunk.GetTile(lx, ly);
    }

    /// <summary>
    /// Assembles an active obstacle collision grid combining terrain and physical props
    /// across the specified local chunk radius.
    /// </summary>
    public TileObstacleGrid CreateActiveObstacleGrid(Vector2 centerWorldPos, int chunkRadius = 2)
    {
        float chunkSizeWorld = Chunk2D.Size * _tileSize;
        int centerChunkX = (int)MathF.Floor(centerWorldPos.X / chunkSizeWorld);
        int centerChunkY = (int)MathF.Floor(centerWorldPos.Y / chunkSizeWorld);

        int minChunkX = centerChunkX - chunkRadius;
        int minChunkY = centerChunkY - chunkRadius;
        int chunksAcross = (chunkRadius * 2) + 1;
        int tilesAcross = chunksAcross * Chunk2D.Size;

        TileObstacleGrid combined = new(tilesAcross, tilesAcross, _tileSize);

        for (int cy = 0; cy < chunksAcross; cy++)
        {
            int chunkY = minChunkY + cy;
            for (int cx = 0; cx < chunksAcross; cx++)
            {
                int chunkX = minChunkX + cx;
                Chunk2D chunk = this.GetOrCreateChunk(chunkX, chunkY);

                int baseOffsetX = cx * Chunk2D.Size;
                int baseOffsetY = cy * Chunk2D.Size;

                for (int ly = 0; ly < Chunk2D.Size; ly++)
                {
                    for (int lx = 0; lx < Chunk2D.Size; lx++)
                    {
                        if (chunk.GetTile(lx, ly).IsSolid)
                        {
                            combined.SetSolid(baseOffsetX + lx, baseOffsetY + ly, true);
                        }
                    }
                }

                for (int i = 0; i < chunk.Props.Count; i++)
                {
                    MapProp prop = chunk.Props[i];
                    if (prop.HasCollision)
                    {
                        int px = Math.Clamp(baseOffsetX + prop.LocalTileX, 0, tilesAcross - 1);
                        int py = Math.Clamp(baseOffsetY + prop.LocalTileY, 0, tilesAcross - 1);
                        combined.SetSolid(px, py, true);
                    }
                }
            }
        }

        return combined;
    }

    /// <summary>
    /// Performs zero-allocation continuous spatial collision resolution for an entity against terrain and physical obstacles.
    /// Follows MMORPG sliding physics: prevents stepping on tree trunks or entering water/ores while allowing
    /// free passage underneath leafy canopies.
    /// </summary>
    /// <param name="position">Entity position in world coordinates, modified in-place to slide around obstacles.</param>
    /// <param name="radius">Entity collision radius.</param>
    /// <param name="pushNormal">Combined surface push normal if a collision occurred; otherwise Vector2.Zero.</param>
    /// <returns>True if a collision was resolved; otherwise false.</returns>
    public bool ResolveEntityCollision(ref Vector2 position, float radius, out Vector2 pushNormal)
    {
        pushNormal = Vector2.Zero;
        bool collided = false;

        // 1. Terrain collision against solid tiles (rivers, ocean, cliffs)
        int minTileX = (int)MathF.Floor((position.X - radius) / _tileSize);
        int maxTileX = (int)MathF.Floor((position.X + radius) / _tileSize);
        int minTileY = (int)MathF.Floor((position.Y - radius) / _tileSize);
        int maxTileY = (int)MathF.Floor((position.Y + radius) / _tileSize);

        for (int ty = minTileY; ty <= maxTileY; ty++)
        {
            for (int tx = minTileX; tx <= maxTileX; tx++)
            {
                MapTile tile = this.GetTileAtWorld(tx, ty);
                if (!tile.IsSolid)
                {
                    continue;
                }

                float tileMinX = tx * _tileSize;
                float tileMinY = ty * _tileSize;
                float tileMaxX = tileMinX + _tileSize;
                float tileMaxY = tileMinY + _tileSize;

                float closestX = Math.Clamp(position.X, tileMinX, tileMaxX);
                float closestY = Math.Clamp(position.Y, tileMinY, tileMaxY);
                Vector2 diff = position - new Vector2(closestX, closestY);
                float distSq = diff.LengthSquared();

                if (distSq < radius * radius)
                {
                    collided = true;
                    if (distSq > 0.0001f)
                    {
                        float dist = MathF.Sqrt(distSq);
                        Vector2 normal = diff / dist;
                        float penetration = radius - dist;
                        position += normal * penetration;
                        pushNormal += normal;
                    }
                    else
                    {
                        Vector2 normal = new(0f, -1f);
                        position += normal * radius;
                        pushNormal += normal;
                    }
                }
            }
        }

        // 2. Physical prop collision (tree trunks, mineral ore boulders, walls)
        // Free passage is permitted underneath tree canopies; collision applies only against the trunk footprint.
        float searchRadius = radius + 48f;
        float chunkSizeWorld = Chunk2D.Size * _tileSize;
        int minChunkX = (int)MathF.Floor((position.X - searchRadius) / chunkSizeWorld);
        int maxChunkX = (int)MathF.Floor((position.X + searchRadius) / chunkSizeWorld);
        int minChunkY = (int)MathF.Floor((position.Y - searchRadius) / chunkSizeWorld);
        int maxChunkY = (int)MathF.Floor((position.Y + searchRadius) / chunkSizeWorld);

        for (int cy = minChunkY; cy <= maxChunkY; cy++)
        {
            for (int cx = minChunkX; cx <= maxChunkX; cx++)
            {
                if (!this.TryGetChunk(cx, cy, out Chunk2D? chunk) || chunk == null)
                {
                    continue;
                }

                for (int i = 0; i < chunk.Props.Count; i++)
                {
                    MapProp prop = chunk.Props[i];
                    if (!prop.HasCollision)
                    {
                        continue;
                    }

                    Vector2 propPos = prop.WorldPosition;
                    Vector2 diff = position - propPos;
                    float minDist = prop.CollisionRadius + radius;
                    float distSq = diff.LengthSquared();

                    if (distSq < minDist * minDist)
                    {
                        collided = true;
                        if (distSq > 0.0001f)
                        {
                            float dist = MathF.Sqrt(distSq);
                            Vector2 normal = diff / dist;
                            float penetration = minDist - dist;
                            position += normal * penetration;
                            pushNormal += normal;
                        }
                        else
                        {
                            Vector2 normal = new(1f, 0f);
                            position += normal * minDist;
                            pushNormal += normal;
                        }
                    }
                }
            }
        }

        // 3. Resolve continuous collision against constructed building structures (walls, closed doors, pillars)
        if (_buildings.ResolveCollision(ref position, radius, _tileSize, out Vector2 buildingPushNormal))
        {
            collided = true;
            pushNormal += buildingPushNormal;
        }

        if (pushNormal.LengthSquared() > 0.0001f)
        {
            pushNormal = Vector2.Normalize(pushNormal);
        }

        return collided;
    }

    private static (int X, int Y) LocateSafeNaturalSpawn(ProceduralWorldGenerator generator, float frequency)
    {
        // Spiral outward from (0, 0) to find pristine natural land in LushPlains
        for (int r = 0; r < 500; r += 4)
        {
            for (int dy = -r; dy <= r; dy += 4)
            {
                for (int dx = -r; dx <= r; dx += 4)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                    {
                        continue;
                    }

                    MapTile t = generator.SampleTile(dx, dy, frequency);
                    if (t.Biome == BiomeType.LushPlains && !t.IsSolid)
                    {
                        return (dx, dy);
                    }
                }
            }
        }

        return (0, 0);
    }

    private static long PackChunkKey(int chunkX, int chunkY) =>
        Chunk2D.PackKey(chunkX, chunkY);

    private static (int X, int Y) UnpackChunkKey(long key) =>
        Chunk2D.UnpackKey(key);
}
