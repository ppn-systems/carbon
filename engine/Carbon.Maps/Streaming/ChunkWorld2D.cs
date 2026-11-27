// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using Carbon.Maps.Biomes;
using Carbon.Maps.Building;
using Carbon.Maps.Data;
using Carbon.Maps.Generators;
using Carbon.Maps.Hydrology;
using Carbon.Maps.Navigation;
using Carbon.Maps.Props;
using Carbon.Physics.Obstacles;
using Carbon.Physics.Raycast;
using Carbon.Physics.Shapes;

namespace Carbon.Maps.Streaming;

/// <summary>
/// High-performance chunk-streaming world coordinator managing infinite procedural 2D terrain.
/// Employs Minecraft-style chunk lifecycle management with object pooling to guarantee zero GC allocations during exploration.
/// </summary>
public sealed class ChunkWorld2D : IWorldPhysics2D
{
    private WaterRules _waterRules = WaterRules.Default;
    private readonly ProceduralWorldGenerator _generator;
    private readonly float _tileSize;
    private readonly float _frequency;
    // Serialises chunk creation, eviction and edits. Reads of resident chunks stay lock-free.
    private readonly Lock _gate = new();
    private readonly ChunkDeltaStore _deltas = new();
    private readonly ChunkStreamer _streamer;
    private readonly BuildingGrid _buildings;
    private readonly ConstructionLedger _construction;
    private readonly TileWalkabilityGrid _navGrid;
    private readonly WorldCollision _collision;
    private readonly Lazy<(int X, int Y)> _spawn;

    /// <summary>
    /// Gets the spatial building grid managing constructed structures, connected walls, and doors.
    /// </summary>
    public BuildingGrid Buildings => _buildings;

    /// <summary>
    /// Gets the store of construction sites (blueprints / frames) and structure ownership records, mirrored into chunk deltas.
    /// </summary>
    public ConstructionLedger Construction => _construction;

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
    /// Gets or sets the water movement and oxygen rules (<c>water.json</c>, ADR-032). The server and the client set
    /// them from the same synced document, so prediction moves swimmers at the server's speed.
    /// </summary>
    public WaterRules WaterRules
    {
        get => _waterRules;
        set => _waterRules = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Gets the world spawn coordinate vector located on safe, natural starter land.
    /// </summary>
    public Vector2 SpawnWorldPosition
    {
        get
        {
            (int sx, int sy) = _spawn.Value;
            return new Vector2((sx * _tileSize) + (_tileSize * 0.5f), (sy * _tileSize) + (_tileSize * 0.5f));
        }
    }

    /// <summary>
    /// Gets the world spawn coordinate in tile units.
    /// </summary>
    public (int X, int Y) SpawnTile => _spawn.Value;

    /// <summary>
    /// Gets a zero-allocation read-only span of currently resident active chunks.
    /// </summary>
    public ReadOnlySpan<Chunk2D> ActiveChunks => _streamer.Active;

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
        _buildings = new BuildingGrid(this.OnStructureChanged, this.OnStructureConnectivityUpdated);
        _construction = new ConstructionLedger(this.OnConstructionSiteChanged, this.OnStructureRecordChanged);

        ProceduralWorldGenerator genRef = _generator;
        float freqCopy = _frequency;
        _spawn = new Lazy<(int X, int Y)>(
            () => freqCopy == genRef.Settings.Frequency ? genRef.SpawnTile : SpawnLocator.Locate(genRef, freqCopy),
            LazyThreadSafetyMode.ExecutionAndPublication);

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
    /// Gets a zero-allocation read-only span of recorded changes of every chunk that has been edited, loaded or not.
    /// </summary>
    public ReadOnlySpan<ChunkDelta> Deltas => _deltas.All;

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
            // Deltas recorded against another generator version are migrated first (see ChunkDelta.MigrateTo).
            delta = delta.MigrateTo(ProceduralWorldGenerator.GeneratorVersion);
            _deltas.Set(delta);
            _buildings.ApplyChunkStructures(delta.ChunkX, delta.ChunkY, delta.Structures);
            _construction.ApplyChunk(delta.ChunkX, delta.ChunkY, delta.Sites, delta.StructureRecords);
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
    public bool TryDamagePropAt(int worldTileX, int worldTileY, float damage, out MapProp updated) =>
        this.TryDamagePropAt(worldTileX, worldTileY, damage, byte.MaxValue, out updated);

    /// <summary>
    /// Applies damage to the natural prop anchored on a world tile when <paramref name="toolHarvestTier"/> meets or
    /// exceeds the prop's required harvest tier, recording edits into the chunk delta.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="damage">Damage amount.</param>
    /// <param name="toolHarvestTier">Harvest tier of the striking tool.</param>
    /// <param name="updated">The prop after damage, or the unchanged prop if harvest tier is insufficient.</param>
    /// <returns><c>true</c> when a prop exists and damage was applied; otherwise <c>false</c>.</returns>
    public bool TryDamagePropAt(int worldTileX, int worldTileY, float damage, byte toolHarvestTier, out MapProp updated) =>
        this.TryDamagePropAt(worldTileX, worldTileY, damage, toolHarvestTier, respawnAtTick: 0, out updated);

    /// <summary>
    /// Applies damage to the natural prop anchored on a world tile when <paramref name="toolHarvestTier"/> meets or
    /// exceeds the prop's required harvest tier, optionally scheduling a respawn tick when destroyed.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="damage">Damage amount.</param>
    /// <param name="toolHarvestTier">Harvest tier of the striking tool.</param>
    /// <param name="respawnAtTick">Tick when destroyed resource regrows (0 = does not auto-respawn).</param>
    /// <param name="updated">The prop after damage, or the unchanged prop if indestructible or tool tier is insufficient.</param>
    /// <returns><c>true</c> when a prop exists and damage was applied; otherwise <c>false</c>.</returns>
    public bool TryDamagePropAt(int worldTileX, int worldTileY, float damage, byte toolHarvestTier, ulong respawnAtTick, out MapProp updated)
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
            if (!current.Destructibility.IsDestructible || toolHarvestTier < current.Destructibility.HarvestTier)
            {
                updated = current;
                return false;
            }

            updated = current with { Destructibility = current.Destructibility.ApplyDamage(damage) };
            chunk.SetProp(index, updated);
            _deltas.GetOrAdd(cx, cy).SetPropState(lx, ly, updated.Destructibility, respawnAtTick);
            return true;
        }
    }

    /// <summary>
    /// Reads the natural prop anchored on <paramref name="worldTileX"/>, <paramref name="worldTileY"/>, if present
    /// (including destroyed props so remnant stumps/rubble and metadata remain inspectable).
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="prop">Resolved prop snapshot.</param>
    /// <returns><see langword="true"/> when a prop exists at the coordinate; otherwise <see langword="false"/>.</returns>
    public bool TryGetPropAt(int worldTileX, int worldTileY, out MapProp prop)
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
                prop = default;
                return false;
            }

            prop = chunk.Props[index];
            return true;
        }
    }

    /// <summary>
    /// Synchronizes an authoritative prop durability and destruction state at a world tile on the client,
    /// returning both previous and updated prop snapshots for fracture VFX and loot/combat feedback.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="currentDurability">Authoritative remaining durability.</param>
    /// <param name="isDestroyed">Whether the prop is destroyed.</param>
    /// <param name="previous">Prop snapshot prior to applying this state update.</param>
    /// <param name="updated">Updated prop snapshot.</param>
    /// <returns><see langword="true"/> if a matching prop exists on the tile; otherwise <see langword="false"/>.</returns>
    public bool TrySetPropStateAt(
        int worldTileX,
        int worldTileY,
        float currentDurability,
        bool isDestroyed,
        out MapProp previous,
        out MapProp updated)
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
                previous = default;
                updated = default;
                return false;
            }

            previous = chunk.Props[index];
            float maxDur = MathF.Max(1f, previous.Destructibility.MaxDurability);
            float clamped = isDestroyed ? 0f : Math.Clamp(currentDurability, 0f, maxDur);
            PropDestructibility nextState = previous.Destructibility with
            {
                CurrentDurability = clamped,
                IsDestroyed = isDestroyed || clamped <= 0f,
            };

            updated = previous with { Destructibility = nextState };
            chunk.SetProp(index, updated);
            _deltas.GetOrAdd(cx, cy).SetPropState(lx, ly, nextState);
            return true;
        }
    }

    /// <summary>
    /// Restores any destroyed props whose scheduled respawn tick has been reached.
    /// </summary>
    /// <param name="currentTick">Current simulation tick.</param>
    /// <returns>The total number of props respawned.</returns>
    public int ProcessPropRespawns(ulong currentTick) =>
        this.ProcessPropRespawns(currentTick, respawnedProps: null);

    /// <summary>
    /// Restores any destroyed props whose scheduled respawn tick has been reached and optionally collects
    /// the restored world tile coordinates &amp; max durability for network replication.
    /// </summary>
    public int ProcessPropRespawns(
        ulong currentTick,
        List<(int TileX, int TileY, uint Revision, float MaxDurability)>? respawnedProps)
    {
        int respawned = 0;
        List<int>? localKeys = respawnedProps is not null ? [] : null;
        lock (_gate)
        {
            ReadOnlySpan<ChunkDelta> all = _deltas.All;
            for (int i = 0; i < all.Length; i++)
            {
                ChunkDelta delta = all[i];
                localKeys?.Clear();
                int expired = delta.RemoveExpiredPropStates(currentTick, localKeys);
                if (expired > 0)
                {
                    respawned += expired;
                    _streamer.RebuildIfResident(delta.ChunkX, delta.ChunkY);

                    if (respawnedProps is not null && localKeys is not null && _streamer.TryGet(delta.ChunkX, delta.ChunkY, out Chunk2D? chunk) && chunk is not null)
                    {
                        int baseTx = delta.ChunkX * Chunk2D.Size;
                        int baseTy = delta.ChunkY * Chunk2D.Size;
                        for (int k = 0; k < localKeys.Count; k++)
                        {
                            int key = localKeys[k];
                            int lx = key & Chunk2D.SizeMask;
                            int ly = key >> Chunk2D.SizeBits;
                            int pIdx = chunk.FindPropIndexAt(lx, ly);
                            float maxDur = pIdx >= 0 ? chunk.Props[pIdx].Destructibility.MaxDurability : 100f;
                            respawnedProps.Add((baseTx + lx, baseTy + ly, delta.Revision, maxDur));
                        }
                    }
                }
            }
        }

        return respawned;
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

    /// <summary>
    /// Checks whether a terrain road occupies <paramref name="worldTileX"/>, <paramref name="worldTileY"/> without generating chunks.
    /// </summary>
    public bool IsRoadAtWorld(int worldTileX, int worldTileY)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        if (_streamer.TryGet(cx, cy, out Chunk2D? chunk) && chunk is not null)
        {
            return chunk.IsRoad(Chunk2D.ToLocalCoord(worldTileX), Chunk2D.ToLocalCoord(worldTileY));
        }

        MapTile tile = this.PeekTileAtWorld(worldTileX, worldTileY);
        return !tile.IsSolid && tile.Biome is not (BiomeType.ShallowWater or BiomeType.DeepOcean) &&
            _generator.Roads.IsRoadOrBridgeAtTile(worldTileX, worldTileY, bridgeOnly: false);
    }

    /// <summary>
    /// Checks whether a wooden bridge spans <paramref name="worldTileX"/>, <paramref name="worldTileY"/> without generating chunks.
    /// </summary>
    public bool IsBridgeAtWorld(int worldTileX, int worldTileY)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        if (_streamer.TryGet(cx, cy, out Chunk2D? chunk) && chunk is not null)
        {
            return chunk.IsBridge(Chunk2D.ToLocalCoord(worldTileX), Chunk2D.ToLocalCoord(worldTileY));
        }

        MapTile tile = this.PeekTileAtWorld(worldTileX, worldTileY);
        return tile.Biome is BiomeType.ShallowWater or BiomeType.DeepOcean &&
            _generator.Roads.IsRoadOrBridgeAtTile(worldTileX, worldTileY, bridgeOnly: true);
    }

    /// <inheritdoc/>
    public float GetMoveSpeedMultiplier(Vector2 worldPosition, uint terrainMask) =>
        TerrainMask.IsWaterBound(terrainMask) ? 1f : this.GetMoveSpeedMultiplier(worldPosition);

    /// <inheritdoc/>
    public float GetMoveSpeedMultiplier(Vector2 worldPosition)
    {
        int tx = (int)MathF.Floor(worldPosition.X / _tileSize);
        int ty = (int)MathF.Floor(worldPosition.Y / _tileSize);
        if (_buildings.TryGetStructure(tx, ty, out StructureTile structure) && !structure.IsSolid && structure.MoveSpeedMultiplier > 0f)
        {
            return structure.MoveSpeedMultiplier;
        }

        if (this.IsBridgeAtWorld(tx, ty) || _generator.Landmarks.IsWalkablePierTile(tx, ty) || this.IsRoadAtWorld(tx, ty))
        {
            return 1.20f;
        }

        MapTile tile = this.PeekTileAtWorld(tx, ty);
        if (tile.Biome == BiomeType.VolcanicAshland && tile.IsSolid)
        {
            return 0f;
        }

        if (WaterWading.IsWater(tile.Biome))
        {
            // Wading, swimming and diving slow with depth (water.json, ADR-032 D10), blended across tiles.
            return this.WaterRules.SpeedAtDepth(this.GetWaterDepthAt(worldPosition));
        }

        return tile.MoveSpeedMultiplier;
    }

    /// <summary>
    /// Gets the water depth (<see cref="WaterDepth"/>) at a position, blended bilinearly between the four nearest tile
    /// centres so it changes smoothly inside a tile. Dry land, bridges and piers report 0. Allocation-free.
    /// </summary>
    /// <param name="worldPosition">Position in world units.</param>
    /// <returns>The depth; 0 out of the water.</returns>
    public float GetWaterDepthAt(Vector2 worldPosition)
    {
        int tx = (int)MathF.Floor(worldPosition.X / _tileSize);
        int ty = (int)MathF.Floor(worldPosition.Y / _tileSize);
        if (!WaterWading.IsWater(this.PeekTileAtWorld(tx, ty).Biome) || this.IsDeckAtWorld(tx, ty))
        {
            return 0f;
        }

        float u = (worldPosition.X / _tileSize) - 0.5f;
        float v = (worldPosition.Y / _tileSize) - 0.5f;
        int x0 = (int)MathF.Floor(u);
        int y0 = (int)MathF.Floor(v);
        float fx = u - x0;
        float fy = v - y0;
        float top = WaterDepth.Of(this.PeekTileAtWorld(x0, y0)) +
            ((WaterDepth.Of(this.PeekTileAtWorld(x0 + 1, y0)) - WaterDepth.Of(this.PeekTileAtWorld(x0, y0))) * fx);
        float bottom = WaterDepth.Of(this.PeekTileAtWorld(x0, y0 + 1)) +
            ((WaterDepth.Of(this.PeekTileAtWorld(x0 + 1, y0 + 1)) - WaterDepth.Of(this.PeekTileAtWorld(x0, y0 + 1))) * fx);
        return MathF.Max(top + ((bottom - top) * fy), WaterDepth.MinDepth);
    }

    /// <summary>Gets the depth tier of the water tile under a position: <see cref="WaterDepthTier.Dry"/> on land, bridges and piers.</summary>
    /// <param name="worldPosition">Position in world units.</param>
    /// <returns>The tier.</returns>
    public WaterDepthTier GetWaterTierAt(Vector2 worldPosition)
    {
        int tx = (int)MathF.Floor(worldPosition.X / _tileSize);
        int ty = (int)MathF.Floor(worldPosition.Y / _tileSize);
        return this.IsDeckAtWorld(tx, ty) ? WaterDepthTier.Dry : WaterDepth.TierOf(this.PeekTileAtWorld(tx, ty));
    }

    /// <summary>Gets how a player moves at a position: walking, wading, swimming or diving (ADR-032 D10).</summary>
    /// <param name="worldPosition">Position in world units.</param>
    /// <returns>The swim state.</returns>
    public SwimState GetSwimStateAt(Vector2 worldPosition) => WaterRules.SwimStateOf(this.GetWaterTierAt(worldPosition));

    private bool IsDeckAtWorld(int worldTileX, int worldTileY) =>
        this.IsBridgeAtWorld(worldTileX, worldTileY) || _generator.Landmarks.IsWalkablePierTile(worldTileX, worldTileY);

    /// <summary>
    /// Gets the normalized wading depth of shallow water at <paramref name="worldPosition"/> (see <see cref="WaterWading"/>).
    /// Dry land, bridges, piers and deep (impassable) water all report 0. Allocation-free; reads at most 25 tiles.
    /// </summary>
    /// <param name="worldPosition">Position in world units.</param>
    /// <returns>Depth in [0, 1]: near 0 at the waterline, 1 at full wading depth.</returns>
    public float GetWaterDepth(Vector2 worldPosition)
    {
        int tx = (int)MathF.Floor(worldPosition.X / _tileSize);
        int ty = (int)MathF.Floor(worldPosition.Y / _tileSize);
        if (this.PeekTileAtWorld(tx, ty).Biome != BiomeType.ShallowWater ||
            this.IsBridgeAtWorld(tx, ty) || _generator.Landmarks.IsWalkablePierTile(tx, ty))
        {
            return 0f;
        }

        const int Reach = 2; // tiles; covers WaterWading.ShoreRampTiles and DeepRampTiles
        float far = Reach * _tileSize;
        float toDry = far;
        float toDeep = far;
        for (int dy = -Reach; dy <= Reach; dy++)
        {
            for (int dx = -Reach; dx <= Reach; dx++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                int nx = tx + dx;
                int ny = ty + dy;
                BiomeType biome = this.PeekTileAtWorld(nx, ny).Biome;
                if (biome == BiomeType.ShallowWater)
                {
                    continue;
                }

                // Distance from the position to the neighbour tile's square.
                float minX = nx * _tileSize;
                float minY = ny * _tileSize;
                float ex = MathF.Max(MathF.Max(minX - worldPosition.X, worldPosition.X - (minX + _tileSize)), 0f);
                float ey = MathF.Max(MathF.Max(minY - worldPosition.Y, worldPosition.Y - (minY + _tileSize)), 0f);
                float dist = MathF.Sqrt((ex * ex) + (ey * ey));
                if (biome == BiomeType.DeepOcean)
                {
                    toDeep = MathF.Min(toDeep, dist);
                }
                else
                {
                    toDry = MathF.Min(toDry, dist);
                }
            }
        }

        return WaterWading.DepthFromDistances(toDry, toDeep, _tileSize);
    }

    /// <inheritdoc/>
    public bool ResolveCircleCollision(ref Vector2 position, float radius, out Vector2 pushNormal) =>
        this.ResolveEntityCollision(ref position, radius, out pushNormal);

    /// <inheritdoc/>
    public bool ResolveCircleCollision(ref Vector2 position, float radius, uint terrainMask, out Vector2 pushNormal) =>
        _collision.Resolve(ref position, radius, terrainMask, out pushNormal);

    /// <inheritdoc/>
    public bool ResolveAabbCollision(ref Vector2 position, in AABB2D localBox, out Vector2 pushNormal) =>
        _collision.ResolveAabb(ref position, in localBox, out pushNormal);

    /// <inheritdoc/>
    public bool Raycast(in Ray2D ray, out RaycastHit2D hit) =>
        _collision.Raycast(in ray, out hit);

    private void OnStructureChanged(int worldTileX, int worldTileY, StructureTile structure)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        int lx = Chunk2D.ToLocalCoord(worldTileX);
        int ly = Chunk2D.ToLocalCoord(worldTileY);

        lock (_gate)
        {
            ChunkDelta delta = _deltas.GetOrAdd(cx, cy);
            if (structure.IsOccupied)
            {
                delta.SetStructure(lx, ly, in structure);
                if (this.TryGetChunk(cx, cy, out Chunk2D? chunk) && chunk is not null)
                {
                    int propIdx = chunk.FindPropIndexAt(lx, ly);
                    if (propIdx >= 0 && chunk.Props[propIdx].Destructibility.IsDestructible && !chunk.Props[propIdx].Destructibility.IsDestroyed)
                    {
                        chunk.SetProp(propIdx, chunk.Props[propIdx] with { Destructibility = chunk.Props[propIdx].Destructibility.ApplyDamage(float.MaxValue) });
                    }
                }
            }
            else
            {
                delta.RemoveStructure(lx, ly);
            }
        }
    }

    private void OnConstructionSiteChanged(int anchorTileX, int anchorTileY, ConstructionSite? site)
    {
        int cx = Chunk2D.ToChunkCoord(anchorTileX);
        int cy = Chunk2D.ToChunkCoord(anchorTileY);
        int lx = Chunk2D.ToLocalCoord(anchorTileX);
        int ly = Chunk2D.ToLocalCoord(anchorTileY);

        lock (_gate)
        {
            ChunkDelta delta = _deltas.GetOrAdd(cx, cy);
            if (site is null)
            {
                delta.RemoveSite(lx, ly);
            }
            else
            {
                delta.SetSite(lx, ly, site);
            }
        }
    }

    private void OnStructureRecordChanged(int worldTileX, int worldTileY, StructureRecord? record)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        int lx = Chunk2D.ToLocalCoord(worldTileX);
        int ly = Chunk2D.ToLocalCoord(worldTileY);

        lock (_gate)
        {
            ChunkDelta delta = _deltas.GetOrAdd(cx, cy);
            if (record is { } value)
            {
                delta.SetStructureRecord(lx, ly, value);
            }
            else
            {
                delta.RemoveStructureRecord(lx, ly);
            }
        }
    }

    private void OnStructureConnectivityUpdated(int worldTileX, int worldTileY, WallConnectivityMask connectivity)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        int lx = Chunk2D.ToLocalCoord(worldTileX);
        int ly = Chunk2D.ToLocalCoord(worldTileY);

        lock (_gate)
        {
            if (_deltas.TryGet(cx, cy, out ChunkDelta? delta) && delta is not null)
            {
                delta.RefreshStructureConnectivity(lx, ly, connectivity);
            }
        }
    }
}
