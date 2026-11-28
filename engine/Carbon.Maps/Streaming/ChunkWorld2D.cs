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
    private readonly List<int> _respawnKeysScratch = [];

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

    /// <summary>Gets the lock guarding chunk and delta changes (tests: background builds never take it, ADR-045 D9).</summary>
    internal Lock Gate => _gate;

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

    /// <summary>ADR-045 D1 <c>K</c>: chunks beyond the load radius whose road sites are planned ahead (one step's travel).</summary>
    internal const int RoadPrewarmMarginChunks = 1;

    /// <summary>
    /// Gets a zero-allocation read-only span of currently resident active chunks.
    /// </summary>
    public ReadOnlySpan<Chunk2D> ActiveChunks => _streamer.Active;

    /// <summary>
    /// Gets the count of currently resident active chunks in memory.
    /// </summary>
    public int LoadedChunkCount => _streamer.ActiveCount;

    /// <summary>Gets the number of resident snapshots the streamer has built (tests: one per streaming step).</summary>
    internal int SnapshotBuilds => _streamer.SnapshotBuilds;

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

        lock (_gate)
        {
            // TS-6: looked up under the lock, which eviction and recycling also take, so it is the chunk of (cx, cy).
            Chunk2D chunk = this.GetOrCreateChunk(cx, cy);
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
            ChunkDelta delta = _deltas.GetOrAdd(cx, cy);
            TreeGrowth growth = delta.GrowthAt(ChunkDelta.ToLocalIndex(lx, ly));
            ulong at = respawnAtTick;
            if (growth.Stage != TreeGrowthStage.Mature && !updated.Destructibility.IsDestroyed && delta.TryGetRespawnTick(lx, ly, out ulong growsAt))
            {
                // A damaged stump or young tree keeps growing on schedule (ADR-040 D1).
                at = growsAt;
            }

            delta.SetPropState(lx, ly, updated.Destructibility, at, growth);
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

        lock (_gate)
        {
            // TS-6: looked up under the lock, which eviction and recycling also take, so it is the chunk of (cx, cy).
            Chunk2D chunk = this.GetOrCreateChunk(cx, cy);
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
        out MapProp updated) =>
        this.TrySetPropStateAt(worldTileX, worldTileY, currentDurability, isDestroyed, TreeGrowthStage.Mature, out previous, out updated);

    /// <summary>
    /// Synchronizes an authoritative prop state at a world tile on the client, including its tree growth stage (ADR-040).
    /// A stage change resets the durability scale to the new stage's durability.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="currentDurability">Authoritative remaining durability.</param>
    /// <param name="isDestroyed">Whether the prop is destroyed.</param>
    /// <param name="stage">The tree growth stage (mature for every other prop).</param>
    /// <param name="previous">Prop snapshot prior to applying this state update.</param>
    /// <param name="updated">Updated prop snapshot.</param>
    /// <returns><see langword="true"/> if a matching prop exists on the tile; otherwise <see langword="false"/>.</returns>
    public bool TrySetPropStateAt(
        int worldTileX,
        int worldTileY,
        float currentDurability,
        bool isDestroyed,
        TreeGrowthStage stage,
        out MapProp previous,
        out MapProp updated)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        int lx = Chunk2D.ToLocalCoord(worldTileX);
        int ly = Chunk2D.ToLocalCoord(worldTileY);

        lock (_gate)
        {
            // TS-6: looked up under the lock, which eviction and recycling also take, so it is the chunk of (cx, cy).
            Chunk2D chunk = this.GetOrCreateChunk(cx, cy);
            int index = chunk.FindPropIndexAt(lx, ly);
            if (index < 0)
            {
                previous = default;
                updated = default;
                return false;
            }

            previous = chunk.Props[index];
            PropDestructibility basis = previous.Destructibility;
            if (stage != previous.GrowthStage)
            {
                // A new stage has its own durability scale; the server's value is the full durability of that stage.
                basis = basis with { MaxDurability = MathF.Max(1f, currentDurability) };
            }

            float maxDur = MathF.Max(1f, basis.MaxDurability);
            float clamped = isDestroyed ? 0f : Math.Clamp(currentDurability, 0f, maxDur);
            PropDestructibility nextState = basis with
            {
                CurrentDurability = clamped,
                IsDestroyed = isDestroyed || clamped <= 0f,
            };

            updated = previous with { Destructibility = nextState, GrowthStage = stage };
            chunk.SetProp(index, updated);
            _deltas.GetOrAdd(cx, cy).SetPropState(lx, ly, nextState, 0UL, stage == TreeGrowthStage.Mature ? default : new TreeGrowth(stage, previous.Type));
            return true;
        }
    }

    /// <summary>
    /// Restores any destroyed props whose scheduled respawn tick has been reached, and grows due trees to their next stage
    /// at once (no growth rules: a growing tree comes back mature).
    /// </summary>
    /// <param name="currentTick">Current simulation tick.</param>
    /// <returns>The total number of props respawned or grown.</returns>
    public int ProcessPropRespawns(ulong currentTick) =>
        this.ProcessPropRespawns(currentTick, rules: null, tickRate: 1, isTileBlocked: null, isTileWatched: null, respawnedProps: null);

    /// <summary>
    /// Restores destroyed props whose respawn tick has been reached and grows due trees one stage (ADR-040 D1: stump,
    /// sapling, young, mature), optionally collecting each changed prop for replication. A prop never comes back or grows
    /// inside a structure, on a construction site, or on a tile <paramref name="isTileBlocked"/> reports (e.g. a player
    /// stands there). A tree also waits while <paramref name="isTileWatched"/> reports a player nearby, so a stage never
    /// changes in front of them. A prop that waits tries again after the rules' blocked retry delay. Allocation-free once
    /// the destination has capacity: a resident chunk is changed in place, and a chunk that is not resident applies the
    /// delta when it next loads.
    /// </summary>
    /// <param name="currentTick">Current simulation tick.</param>
    /// <param name="rules">Regrowth rules (stage durations and durabilities, retry delay); <see langword="null"/>: due trees come back mature and blocked props retry next tick.</param>
    /// <param name="tickRate">World ticks per second.</param>
    /// <param name="isTileBlocked">Optional check of a world tile (X, Y) the caller knows is occupied.</param>
    /// <param name="isTileWatched">Optional check of whether a player is close enough to a world tile to see a tree grow.</param>
    /// <param name="respawnedProps">Optional destination for the changed props: tile, chunk revision, durability and stage.</param>
    /// <returns>The total number of props respawned or grown.</returns>
    public int ProcessPropRespawns(
        ulong currentTick,
        PropRegrowthRules? rules,
        int tickRate,
        Func<int, int, bool>? isTileBlocked,
        Func<int, int, bool>? isTileWatched,
        List<(int TileX, int TileY, uint Revision, float Durability, TreeGrowthStage Stage)>? respawnedProps)
    {
        int respawned = 0;
        ulong retryAt = currentTick + (rules?.BlockedRetryTicks(tickRate) ?? 1UL);
        lock (_gate)
        {
            ReadOnlySpan<ChunkDelta> all = _deltas.All;
            for (int i = 0; i < all.Length; i++)
            {
                ChunkDelta delta = all[i];
                if (delta.PendingRespawnCount == 0)
                {
                    continue;
                }

                _respawnKeysScratch.Clear();
                if (delta.CollectDueRespawns(currentTick, _respawnKeysScratch) == 0)
                {
                    continue;
                }

                bool resident = _streamer.TryGet(delta.ChunkX, delta.ChunkY, out Chunk2D? chunk) && chunk is not null;
                int baseTx = delta.ChunkX * Chunk2D.Size;
                int baseTy = delta.ChunkY * Chunk2D.Size;
                for (int k = 0; k < _respawnKeysScratch.Count; k++)
                {
                    int key = _respawnKeysScratch[k];
                    int lx = key & Chunk2D.SizeMask;
                    int ly = key >> Chunk2D.SizeBits;
                    int tx = baseTx + lx;
                    int ty = baseTy + ly;
                    TreeGrowth growth = delta.GrowthAt(key);
                    if ((_buildings.TryGetStructure(tx, ty, out StructureTile structure) && structure.IsOccupied) ||
                        _construction.IsReserved(tx, ty) ||
                        (isTileBlocked is not null && isTileBlocked(tx, ty)) ||
                        (growth.Stage != TreeGrowthStage.Mature && isTileWatched is not null && isTileWatched(tx, ty)))
                    {
                        delta.DeferRespawn(key, retryAt);
                        continue;
                    }

                    int pIdx = resident ? chunk!.FindPropIndexAt(lx, ly) : -1;
                    TreeGrowthStage next = TreeGrowthRules.NextStage(growth.Stage);
                    if (growth.Stage != TreeGrowthStage.Mature && next != TreeGrowthStage.Mature && rules is not null)
                    {
                        // One stage further: the delta keeps the tree type, so this works while the chunk is not loaded.
                        PropDestructibility grown = rules.Trees.StateOf(growth.Type, next);
                        delta.SetPropState(lx, ly, grown, currentTick + rules.Trees.StageTicks(next, tickRate), new TreeGrowth(next, growth.Type));
                        if (pIdx >= 0)
                        {
                            chunk!.SetProp(pIdx, chunk.Props[pIdx] with { Destructibility = grown, GrowthStage = next });
                        }

                        respawned++;
                        respawnedProps?.Add((tx, ty, delta.Revision, grown.CurrentDurability, next));
                        continue;
                    }

                    if (!delta.CompleteRespawn(key))
                    {
                        continue;
                    }

                    respawned++;
                    float durability = growth.Stage != TreeGrowthStage.Mature ? MapProp.BaselineDestructibility(growth.Type).MaxDurability : 100f;
                    if (pIdx >= 0)
                    {
                        MapProp prop = chunk!.Props[pIdx];
                        PropDestructibility baseline = MapProp.BaselineDestructibility(prop.Type);
                        durability = baseline.MaxDurability;
                        chunk.SetProp(pIdx, prop with { Destructibility = baseline, GrowthStage = TreeGrowthStage.Mature });
                    }

                    respawnedProps?.Add((tx, ty, delta.Revision, durability, TreeGrowthStage.Mature));
                }
            }

            _respawnKeysScratch.Clear();
        }

        return respawned;
    }

    /// <summary>
    /// Sets the tree on a world tile to a growth stage (ADR-040 D1): a felled tree becomes a stump, and a dug-up stump is
    /// destroyed for good. Records it in the chunk delta with the tick it grows into the next stage.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="stage">The stage.</param>
    /// <param name="state">The prop state at that stage (destroyed for a dug-up stump).</param>
    /// <param name="nextStageTick">The tick it grows into the next stage (0 = never).</param>
    /// <param name="updated">The prop after the change.</param>
    /// <returns><see langword="true"/> when a tree stands on the tile.</returns>
    public bool TrySetTreeStageAt(int worldTileX, int worldTileY, TreeGrowthStage stage, PropDestructibility state, ulong nextStageTick, out MapProp updated)
    {
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        int lx = Chunk2D.ToLocalCoord(worldTileX);
        int ly = Chunk2D.ToLocalCoord(worldTileY);
        lock (_gate)
        {
            // TS-6: looked up under the lock, which eviction and recycling also take, so it is the chunk of (cx, cy).
            Chunk2D chunk = this.GetOrCreateChunk(cx, cy);
            int index = chunk.FindPropIndexAt(lx, ly);
            if (index < 0 || !MapProp.IsTree(chunk.Props[index].Type))
            {
                updated = default;
                return false;
            }

            updated = chunk.Props[index] with { Destructibility = state, GrowthStage = stage };
            chunk.SetProp(index, updated);
            _deltas.GetOrAdd(cx, cy).SetPropState(lx, ly, state, nextStageTick, new TreeGrowth(stage, updated.Type));
            return true;
        }
    }

    /// <summary>
    /// Plants a sapling on a world tile (ADR-040 D1): natural ground (no water, rock, road or bridge) in a biome that grows
    /// trees, with no structure, construction site or other prop on it. A cleared tree's tile (a dug-up stump, a cut sapling
    /// or young tree) can be planted again. The
    /// sapling grows into the biome's tree (a dug-up stump's tile: the tree it was) on the rules' stage times.
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <param name="rules">Regrowth rules (the sapling stage's durability and time).</param>
    /// <param name="currentTick">Current simulation tick.</param>
    /// <param name="tickRate">World ticks per second.</param>
    /// <param name="planted">The sapling prop, when planted.</param>
    /// <returns>The outcome.</returns>
    public PlantResult TryPlantTree(int worldTileX, int worldTileY, PropRegrowthRules rules, ulong currentTick, int tickRate, out MapProp planted)
    {
        ArgumentNullException.ThrowIfNull(rules);
        int cx = Chunk2D.ToChunkCoord(worldTileX);
        int cy = Chunk2D.ToChunkCoord(worldTileY);
        int lx = Chunk2D.ToLocalCoord(worldTileX);
        int ly = Chunk2D.ToLocalCoord(worldTileY);
        lock (_gate)
        {
            // TS-6: looked up under the lock, which eviction and recycling also take, so it is the chunk of (cx, cy).
            Chunk2D chunk = this.GetOrCreateChunk(cx, cy);
            planted = default;
            MapTile tile = chunk.GetTile(lx, ly);
            // Water biomes grow no trees, so the biome check also refuses water.
            if (tile.IsSolid || chunk.IsRoad(lx, ly) || chunk.IsBridge(lx, ly) ||
                !BiomePropTables.TryGetPlantedTree(tile.Biome, out PropRule tree))
            {
                return PlantResult.UnsuitableGround;
            }

            if ((_buildings.TryGetStructure(worldTileX, worldTileY, out StructureTile structure) && structure.IsOccupied) ||
                _construction.IsReserved(worldTileX, worldTileY))
            {
                return PlantResult.Occupied;
            }

            PropDestructibility sapling = rules.Trees.StateOf(tree.Type, TreeGrowthStage.Sapling);
            ulong growsAt = currentTick + rules.Trees.StageTicks(TreeGrowthStage.Sapling, tickRate);
            ChunkDelta delta = _deltas.GetOrAdd(cx, cy);
            int index = chunk.FindPropIndexAt(lx, ly);
            if (index >= 0)
            {
                MapProp existing = chunk.Props[index];
                // A tree cleared at a growing stage (a dug-up stump, a cut sapling or young tree) left the tile free.
                bool cleared = MapProp.IsTree(existing.Type) && existing.Destructibility.IsDestroyed && existing.GrowthStage != TreeGrowthStage.Mature;
                if (!cleared)
                {
                    return PlantResult.Occupied;
                }

                sapling = rules.Trees.StateOf(existing.Type, TreeGrowthStage.Sapling);
                planted = existing with { Destructibility = sapling, GrowthStage = TreeGrowthStage.Sapling };
                chunk.SetProp(index, planted);
            }
            else
            {
                planted = BiomePropTables.CreatePlantedTree(tree.Type, lx, ly, worldTileX, worldTileY, _tileSize) with
                {
                    Destructibility = sapling,
                    GrowthStage = TreeGrowthStage.Sapling,
                };
                chunk.AddProp(planted);
                delta.SetPlanted(lx, ly, tree.Type);
            }

            delta.SetPropState(lx, ly, sapling, growsAt, new TreeGrowth(TreeGrowthStage.Sapling, planted.Type));
            return PlantResult.Planted;
        }
    }

    /// <summary>
    /// Gets whether a tree that is not full grown stands on the tile (a stump or a sapling). It does not block movement, but
    /// nothing is built on it: dig the stump up first (ADR-040 D1).
    /// </summary>
    /// <param name="worldTileX">World tile X.</param>
    /// <param name="worldTileY">World tile Y.</param>
    /// <returns><see langword="true"/> for a standing stump or sapling.</returns>
    public bool HasGrowingTreeAt(int worldTileX, int worldTileY) =>
        this.TryGetPropAt(worldTileX, worldTileY, out MapProp prop) &&
        !prop.Destructibility.IsDestroyed &&
        prop.GrowthStage is TreeGrowthStage.Stump or TreeGrowthStage.Sapling;

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

    /// <summary>Releases what <paramref name="stream"/> kept cached or resident (a player left the world).</summary>
    /// <param name="stream">The stream passed to <see cref="UpdateAroundPosition"/>.</param>
    public void ReleaseStream(ulong stream)
    {
        _streamer.Release(stream);
        _generator.ReleaseStream(stream);
    }

    /// <summary>
    /// Streams and loads all chunks within <paramref name="chunkRadius"/> around the target world position,
    /// recycling chunks farther than <paramref name="evictionRadius"/> back into the pool, unless another
    /// <paramref name="stream"/> (player) still keeps them within its own eviction radius.
    /// </summary>
    /// <param name="centerWorldPos">Target center world coordinates (e.g. camera or player).</param>
    /// <param name="chunkRadius">Active load radius in chunks (e.g. 4 chunks = 9x9 active grid).</param>
    /// <param name="evictionRadius">Distance in chunks beyond which inactive chunks are recycled into the pool (e.g. 7 chunks).</param>
    /// <param name="stream">Who is streaming: one per player on the server (its connection), 0 on the client.</param>
    public void UpdateAroundPosition(Vector2 centerWorldPos, int chunkRadius = 4, int evictionRadius = 7, ulong stream = 0)
    {
        float chunkSizeWorld = Chunk2D.Size * _tileSize;
        int centerChunkX = (int)MathF.Floor(centerWorldPos.X / chunkSizeWorld);
        int centerChunkY = (int)MathF.Floor(centerWorldPos.Y / chunkSizeWorld);

        // 0. Start building river regions a little beyond the load radius in the background.
        _generator.PrewarmHydrology(
            (centerChunkX * Chunk2D.Size) + (Chunk2D.Size / 2),
            (centerChunkY * Chunk2D.Size) + (Chunk2D.Size / 2),
            (chunkRadius + 8) * Chunk2D.Size,
            stream);

        // 0b. ADR-045 D1: plan, in the background, the road sites of the loaded chunks and of a one-chunk margin, so the
        // chunks the next step loads (a player moves at most one chunk per step) find their sites queued a step ahead.
        (int roadMinX, int roadMinY, int roadMaxX, int roadMaxY) = RoadPrewarmTiles(centerChunkX, centerChunkY, chunkRadius);
        _generator.PrewarmRoads(roadMinX, roadMinY, roadMaxX, roadMaxY, stream);

        // 1. Load the chunks in range; recycle those far away (their edits stay in the delta store).
        // A chunk another stream (player) still keeps is not evicted by this one.
        _streamer.Stream(centerChunkX, centerChunkY, chunkRadius, evictionRadius, stream);
    }

    /// <summary>
    /// The tile rectangle whose road sites <see cref="UpdateAroundPosition"/> plans ahead (ADR-045 D1): the load radius
    /// plus <see cref="RoadPrewarmMarginChunks"/>. Without the margin, a step that crosses a road site boundary loads edge
    /// chunks whose query reads a site row the previous step never queued.
    /// </summary>
    /// <param name="centerChunkX">Centre chunk X.</param>
    /// <param name="centerChunkY">Centre chunk Y.</param>
    /// <param name="chunkRadius">Load radius in chunks.</param>
    /// <returns>The inclusive tile rectangle.</returns>
    internal static (int MinX, int MinY, int MaxX, int MaxY) RoadPrewarmTiles(int centerChunkX, int centerChunkY, int chunkRadius)
    {
        int reach = chunkRadius + RoadPrewarmMarginChunks;
        return (
            (centerChunkX - reach) * Chunk2D.Size,
            (centerChunkY - reach) * Chunk2D.Size,
            ((centerChunkX + reach + 1) * Chunk2D.Size) - 1,
            ((centerChunkY + reach + 1) * Chunk2D.Size) - 1);
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

        lock (_gate)
        {
            // TS-6: looked up under the lock, which eviction and recycling also take, so it is the chunk of (cx, cy).
            Chunk2D chunk = this.GetOrCreateChunk(cx, cy);
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
