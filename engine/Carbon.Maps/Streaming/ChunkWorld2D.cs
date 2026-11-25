// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Navigation;
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
    // Serialises chunk creation, eviction and edits. Reads of resident chunks stay lock-free.
    private readonly Lock _gate = new();
    private readonly ChunkDeltaStore _deltas = new();
    private readonly ChunkStreamer _streamer;
    private readonly BuildingGrid _buildings = new();
    private readonly TileWalkabilityGrid _navGrid;
    private readonly WorldCollision _collision;

    private readonly int _spawnTileX;
    private readonly int _spawnTileY;
    private readonly Vector2 _spawnWorldPosition;

    /// <summary>
    /// Gets the spatial building grid managing constructed structures, connected walls, and doors.
    /// </summary>
    public BuildingGrid Buildings => _buildings;

    /// <summary>
    /// Gets the spatial navigation grid managing terrain walkability, pathfinding traversal costs, and cliffs.
    /// </summary>
    public TileWalkabilityGrid NavGrid => _navGrid;

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
    public ICollection<Chunk2D> ActiveChunks => _streamer.Active;

    /// <summary>
    /// Gets the count of currently resident active chunks in memory.
    /// </summary>
    public int LoadedChunkCount => _streamer.ActiveCount;

    /// <summary>
    /// Gets the count of idle recycled chunks currently held in the object pool.
    /// </summary>
    public int PooledChunkCount => _streamer.PooledCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChunkWorld2D"/> class.
    /// Automatically locates a natural safe starter location within a grand LushPlains biome.
    /// </summary>
    /// <param name="generator">The procedural terrain generator.</param>
    /// <param name="tileSize">Physical dimension of each tile (default: the generator's settings).</param>
    /// <param name="frequency">Macro spatial noise frequency (default: the generator's settings).</param>
    public ChunkWorld2D(ProceduralWorldGenerator generator, float? tileSize = null, float? frequency = null)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _tileSize = tileSize ?? generator.Settings.TileSize;
        _frequency = frequency ?? generator.Settings.Frequency;
        _streamer = new ChunkStreamer(generator, _deltas, _gate, _frequency, _tileSize);

        (int sx, int sy) = SpawnLocator.Locate(generator, _frequency);
        _spawnTileX = sx;
        _spawnTileY = sy;
        _spawnWorldPosition = new Vector2((sx * _tileSize) + (_tileSize * 0.5f), (sy * _tileSize) + (_tileSize * 0.5f));
        _navGrid = new TileWalkabilityGrid(this);
        _collision = new WorldCollision(this);
    }

    /// <summary>
    /// Retrieves an existing chunk or generates one using object pooling to eliminate GC pressure.
    /// </summary>
    /// <param name="chunkX">Chunk X coordinate.</param>
    /// <param name="chunkY">Chunk Y coordinate.</param>
    /// <returns>Populated <see cref="Chunk2D"/>.</returns>
    public Chunk2D GetOrCreateChunk(int chunkX, int chunkY) => _streamer.GetOrCreate(chunkX, chunkY);

    /// <summary>
    /// Gets the recorded changes of every chunk that has been edited, loaded or not.
    /// </summary>
    public ICollection<ChunkDelta> Deltas => _deltas.All;

    /// <summary>
    /// Gets the store of player-made changes.
    /// </summary>
    public ChunkDeltaStore DeltaStore => _deltas;

    /// <summary>
    /// Gets the recorded changes of one chunk.
    /// </summary>
    /// <param name="chunkX">Chunk X coordinate.</param>
    /// <param name="chunkY">Chunk Y coordinate.</param>
    /// <param name="delta">The delta, when the chunk has been edited.</param>
    /// <returns><c>true</c> when the chunk has a delta.</returns>
    public bool TryGetDelta(int chunkX, int chunkY, out ChunkDelta? delta) =>
        _deltas.TryGet(chunkX, chunkY, out delta);

    /// <summary>
    /// Imports a delta loaded from storage or received from the server, replacing any existing one.
    /// When the chunk is resident it is regenerated so the delta takes effect immediately.
    /// </summary>
    /// <param name="delta">The delta to import.</param>
    public void ImportDelta(ChunkDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        lock (_gate)
        {
            _deltas.Set(delta);
            _streamer.RebuildIfResident(delta.ChunkX, delta.ChunkY);
        }
    }

    /// <summary>
    /// Applies damage to the natural prop anchored on a world tile (chopping, mining) and records it in the
    /// chunk delta so the result survives chunk eviction.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="damage">Damage amount.</param>
    /// <param name="updated">The prop after damage.</param>
    /// <returns><c>true</c> when a prop exists on the tile.</returns>
    public bool TryDamagePropAt(int worldTileX, int worldTileY, float damage, out MapProp updated)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        int lx = Chunk2D.ToLocalCoord(worldTileX);
        int ly = Chunk2D.ToLocalCoord(worldTileY);

        Chunk2D chunk = this.GetOrCreateChunk(cx, cy);
        lock (_gate)
        {
            int index = chunk.FindPropIndexAt(lx, ly);
            if (index < 0)
            {
                updated = default;
                return false;
            }

            MapProp current = chunk.Props[index];
            updated = current with { Destructibility = current.Destructibility.ApplyDamage(damage) };
            chunk.SetProp(index, updated);
            _deltas.GetOrAdd(cx, cy).SetPropState(lx, ly, updated.Destructibility);
            return true;
        }
    }

    /// <summary>
    /// Reads the terrain tile at a world tile coordinate WITHOUT loading or generating a chunk.
    /// Resident chunks are read directly; otherwise the recorded delta or the pure seed sample is used.
    /// Allocation-free, intended for collision and navigation hot paths.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <returns>The tile.</returns>
    public MapTile PeekTileAtWorld(int worldTileX, int worldTileY)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        int lx = Chunk2D.ToLocalCoord(worldTileX);
        int ly = Chunk2D.ToLocalCoord(worldTileY);
        if (_streamer.TryGet(cx, cy, out Chunk2D? chunk) && chunk != null)
        {
            return chunk.GetTile(lx, ly);
        }

        if (_deltas.TryGetTile(cx, cy, lx, ly, out MapTile edited))
        {
            return edited;
        }

        return _generator.SampleTile(worldTileX, worldTileY, _frequency);
    }

    /// <summary>
    /// Attempts to retrieve a loaded chunk without generating it if absent.
    /// </summary>
    public bool TryGetChunk(int chunkX, int chunkY, out Chunk2D? chunk) =>
        _streamer.TryGet(chunkX, chunkY, out chunk);

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

        // 0. Start building river regions a little beyond the load radius in the background.
        _generator.PrewarmHydrology(
            (centerChunkX * Chunk2D.Size) + (Chunk2D.Size / 2),
            (centerChunkY * Chunk2D.Size) + (Chunk2D.Size / 2),
            (chunkRadius + 8) * Chunk2D.Size);

        // 1. Load the chunks in range; recycle those far away (their edits stay in the delta store).
        _streamer.Stream(centerChunkX, centerChunkY, chunkRadius, evictionRadius);
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
    /// Overwrites or updates the terrain tile at an arbitrary world tile coordinate.
    /// </summary>
    public void SetTileAtWorld(int worldTileX, int worldTileY, in MapTile tile)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        int lx = Chunk2D.ToLocalCoord(worldTileX);
        int ly = Chunk2D.ToLocalCoord(worldTileY);

        Chunk2D chunk = this.GetOrCreateChunk(cx, cy);
        lock (_gate)
        {
            chunk.SetTile(lx, ly, tile);
            _deltas.GetOrAdd(cx, cy).SetTile(lx, ly, tile);
        }
    }

    /// <summary>
    /// Evaluates whether an active natural prop with physical collision occupies the specified world tile.
    /// Chopped trees or mined ores with destroyed status are automatically treated as non-solid.
    /// </summary>
    public bool HasSolidPropAt(int worldTileX, int worldTileY)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        if (!this.TryGetChunk(cx, cy, out Chunk2D? chunk) || chunk == null)
        {
            return false;
        }

        return chunk.HasSolidPropAt(Chunk2D.ToLocalCoord(worldTileX), Chunk2D.ToLocalCoord(worldTileY));
    }

    /// <summary>
    /// Assembles an active obstacle collision grid combining terrain and physical props
    /// across the specified local chunk radius.
    /// </summary>
    public TileObstacleGrid CreateActiveObstacleGrid(Vector2 centerWorldPos, int chunkRadius = 2) =>
        _collision.CreateObstacleGrid(centerWorldPos, chunkRadius);

    /// <summary>
    /// Performs zero-allocation continuous spatial collision resolution for an entity against terrain,
    /// natural props, points of interest and constructed buildings.
    /// </summary>
    /// <param name="position">Entity position in world coordinates, modified in-place to slide around obstacles.</param>
    /// <param name="radius">Entity collision radius.</param>
    /// <param name="pushNormal">Combined surface push normal if a collision occurred; otherwise Vector2.Zero.</param>
    /// <returns>True if a collision was resolved; otherwise false.</returns>
    public bool ResolveEntityCollision(ref Vector2 position, float radius, out Vector2 pushNormal) =>
        _collision.Resolve(ref position, radius, out pushNormal);
}
