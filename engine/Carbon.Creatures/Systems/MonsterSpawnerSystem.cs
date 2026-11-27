// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Carbon.Creatures.Components;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;
using Carbon.Creatures.Spawning;
using Carbon.Maps.Biomes;
using Carbon.Maps.Hydrology;
using Carbon.Primitives.Random;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.Spatial;
using Carbon.Simulation.World;

namespace Carbon.Creatures.Systems;

/// <summary>
/// Deterministic, biome-aware monster spawner. Every <see cref="SpawningConfig.ScanIntervalTicks"/> it visits the
/// density cells around each <see cref="SpawnAnchorComponent"/> entity and, per cell, spawns from the biome's weighted
/// table while the live count is below the (night-boosted) cap and the cell's spawn / respawn timer has elapsed.
/// Never spawns inside the safe zone around the world origin. Randomness is a keyed PRF of the server's secret roll key
/// and <c>(cell, attempt, tick)</c> (ADR-026), so the same key and anchor history always yields the same spawns while a
/// client that knows the world seed cannot predict elites or placements. Night (from <see cref="TimeOfDayHours"/>, an input
/// set by the host) raises stats, density, spawn rate, and elite chance. Runs in <see cref="SimulationPhase.PostUpdate"/>.
/// </summary>
public sealed class MonsterSpawnerSystem : ISimulationSystem
{
    private const int MaxAnchors = 64;

    private readonly MonsterCatalog _catalog;
    private readonly SpawningConfig _config;
    private readonly MonsterScaling _scaling;
    private readonly ISpawnSurface _surface;
    private readonly Vector2 _worldOrigin;
    private readonly IGameplayEntropy _entropy;
    private readonly SpawnTableData?[] _tables = new SpawnTableData?[byte.MaxValue + 1];
    private readonly SpawnTableData?[] _waterTables = new SpawnTableData?[(int)WaterDepthTier.Abyss + 1];
    private readonly Dictionary<CellCoord, CellState> _cells = new(256);
    private readonly HashSet<CellCoord> _visited = new(64);
    private readonly List<TrackedMonster> _tracked = new(256);
    private bool _scanRequested;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonsterSpawnerSystem"/> class.
    /// </summary>
    /// <param name="catalog">Monster catalog.</param>
    /// <param name="config">Spawning configuration.</param>
    /// <param name="surface">Spawn surface (walkability + biome).</param>
    /// <param name="worldOrigin">World origin / first player spawn point; distance from it drives monster level.</param>
    /// <param name="entropy">Secret roll key source for spawn placement, species and elite rolls (ADR-026; never the world seed).</param>
    /// <exception cref="ArgumentException">Thrown when a spawn table references an unknown monster.</exception>
    public MonsterSpawnerSystem(MonsterCatalog catalog, SpawningConfig config, ISpawnSurface surface, Vector2 worldOrigin, IGameplayEntropy entropy)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(entropy);

        _catalog = catalog;
        _config = config;
        _scaling = new MonsterScaling(config);
        _surface = surface;
        _worldOrigin = worldOrigin;
        _entropy = entropy;

        for (int t = 0; t < config.SpawnTables.Count; t++)
        {
            SpawnTable table = config.SpawnTables[t];
            if (_tables[(byte)table.Biome] is not null)
            {
                throw new ArgumentException($"Duplicate spawn table for biome '{table.Biome}'.", nameof(config));
            }

            _tables[(byte)table.Biome] = SpawnTableData.Create(catalog, table.Entries, table.Biome.ToString());
            RequireHabitat(catalog, table.Entries, table.Biome.ToString(), tier: null);
        }

        for (int t = 0; t < config.WaterSpawnTables.Count; t++)
        {
            WaterSpawnTable table = config.WaterSpawnTables[t];
            if (_waterTables[(int)table.Tier] is not null)
            {
                throw new ArgumentException($"Duplicate water spawn table for tier '{table.Tier}'.", nameof(config));
            }

            _waterTables[(int)table.Tier] = SpawnTableData.Create(catalog, table.Entries, table.Tier.ToString());
            RequireHabitat(catalog, table.Entries, table.Tier.ToString(), table.Tier);
        }
    }

    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.PostUpdate;

    /// <summary>Gets or sets the authoritative time of day in hours (<c>0..24</c>), supplied by the host each tick.</summary>
    public float TimeOfDayHours { get; set; } = 12f;

    /// <summary>Gets a value indicating whether <see cref="TimeOfDayHours"/> is inside the configured night window.</summary>
    public bool IsNight => _config.Night.IsNight(this.TimeOfDayHours);

    /// <summary>Gets the scaling model used for spawned monsters.</summary>
    public MonsterScaling Scaling => _scaling;

    /// <summary>Gets the number of live monsters owned by this spawner.</summary>
    public int LiveMonsterCount => _tracked.Count;

    /// <summary>
    /// Requests a scan on the next tick regardless of <see cref="SpawningConfig.ScanIntervalTicks"/>
    /// (e.g. right after a player enters the world, so nearby cells populate immediately).
    /// </summary>
    public void RequestScan() => _scanRequested = true;

    /// <summary>
    /// Converts a world position to its spawner density cell.
    /// </summary>
    /// <param name="position">World position.</param>
    /// <returns>The density cell.</returns>
    public CellCoord GetCell(Vector2 position) =>
        new((int)MathF.Floor(position.X / _config.CellSize), (int)MathF.Floor(position.Y / _config.CellSize));

    /// <summary>
    /// Gets the number of live monsters owned by <paramref name="cell"/>.
    /// </summary>
    /// <param name="cell">Density cell.</param>
    /// <returns>Live monster count.</returns>
    public int GetLiveCount(CellCoord cell) => _cells.TryGetValue(cell, out CellState state) ? state.Alive : 0;

    /// <summary>
    /// Gets the current per-cell cap (night-boosted).
    /// </summary>
    /// <returns>Maximum live monsters per cell.</returns>
    public int GetCellCap() => this.IsNight
        ? (int)MathF.Ceiling(_config.BaseCellCap * _config.Night.DensityMultiplier)
        : _config.BaseCellCap;

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!_scanRequested && tick.TickIndex % _config.ScanIntervalTicks != 0)
        {
            return;
        }

        _scanRequested = false;

        this.ReapDead(world, tick.TickIndex);

        EntityManager entities = world.Entities;
        ComponentPool<SpawnAnchorComponent> anchorPool = entities.GetPool<SpawnAnchorComponent>();
        ReadOnlySpan<int> anchorSlots = anchorPool.EntitySlots;
        Span<Vector2> anchors = stackalloc Vector2[MaxAnchors];
        int anchorCount = 0;
        for (int i = 0; i < anchorSlots.Length && anchorCount < MaxAnchors; i++)
        {
            Entity anchor = entities.GetEntity(anchorSlots[i]);
            if (anchor.IsValid && entities.HasTransform(anchor))
            {
                anchors[anchorCount++] = entities.GetTransform(anchor).Position;
            }
        }

        if (anchorCount == 0)
        {
            return;
        }

        bool isNight = this.IsNight;
        int cap = this.GetCellCap();
        int radius = _config.ActivationRadiusCells;
        _visited.Clear();

        for (int a = 0; a < anchorCount; a++)
        {
            CellCoord center = this.GetCell(anchors[a]);
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    CellCoord cell = new(center.X + dx, center.Y + dy);
                    if (_visited.Add(cell))
                    {
                        this.TrySpawnInCell(world, cell, anchors[..anchorCount], tick.TickIndex, isNight, cap);
                    }
                }
            }
        }
    }

    private void TrySpawnInCell(
        WorldSimulation world,
        CellCoord cell,
        ReadOnlySpan<Vector2> anchors,
        ulong tickIndex,
        bool isNight,
        int cap)
    {
        ref CellState state = ref CollectionsMarshal.GetValueRefOrAddDefault(_cells, cell, out _);
        if (state.Alive >= cap || tickIndex < state.NextSpawnTick)
        {
            return;
        }

        float densityRate = isNight ? _config.Night.DensityMultiplier : 1f;
        state.NextSpawnTick = tickIndex + Math.Max(1UL, (ulong)(_config.SpawnIntervalTicks / densityRate));

        // A rejected candidate (safe zone, too close to a player, blocked tile, no eligible monster) retries at once
        // instead of waiting a whole spawn interval, so cells around a player populate on the first scan.
        int candidates = Math.Max(1, _config.PlacementAttempts);
        for (int c = 0; c < candidates; c++)
        {
            DeterministicRandom rng = _entropy.KeyAt(tickIndex).CreateRandom(
                GameplayDomain.Spawn,
                ((ulong)(uint)cell.X << 32) | (uint)cell.Y,
                state.Attempts,
                tickIndex);
            state.Attempts++;
            if (this.TrySpawnCandidate(world, cell, anchors, isNight, ref rng))
            {
                state.Alive++;
                return;
            }
        }
    }

    private bool TrySpawnCandidate(WorldSimulation world, CellCoord cell, ReadOnlySpan<Vector2> anchors, bool isNight, ref DeterministicRandom rng)
    {
        float size = _config.CellSize;
        Vector2 position = new((cell.X + rng.NextFloat()) * size, (cell.Y + rng.NextFloat()) * size);
        float originDistance = Vector2.Distance(position, _worldOrigin);
        if (originDistance < _config.SafeZoneRadius)
        {
            return false;
        }

        float minAnchorDistSq = _config.MinSpawnDistanceFromAnchor * _config.MinSpawnDistanceFromAnchor;
        for (int i = 0; i < anchors.Length; i++)
        {
            if (Vector2.DistanceSquared(position, anchors[i]) < minAnchorDistSq)
            {
                return false;
            }
        }

        SpawnTableData? table;
        if (_surface.TrySampleSpawnBiome(position, out BiomeType biome))
        {
            table = _tables[(byte)biome];
        }
        else if (_surface.TrySampleSpawnWater(position, out WaterDepthTier tier))
        {
            table = _waterTables[(int)tier]; // ADR-032 D14: water creatures of that depth tier
        }
        else
        {
            return false;
        }

        // Level depends only on distance from the origin; monsters spawn only inside their level range (loot bands).
        int level = (int)MathF.Floor(_scaling.EvaluateLevel(originDistance, out _));
        if (table is null || !table.TryPick(rng.NextFloat(), isNight, level, out ushort monsterIndex))
        {
            return false;
        }

        float eliteChance = isNight ? _config.Night.EliteChance : _config.DayEliteChance;
        if (rng.NextFloat() < eliteChance &&
            _catalog.TryGetEliteVariant(monsterIndex, out ushort eliteIndex) &&
            _catalog.GetDefinition(eliteIndex).IsSpawnableAt(level))
        {
            monsterIndex = eliteIndex;
        }

        ScaledMonsterStats stats = _scaling.Compute(originDistance, isNight, _catalog.GetDefinition(monsterIndex));
        Entity monster = MonsterFactory.Spawn(
            world,
            _catalog,
            monsterIndex,
            position,
            in stats,
            _config.MonsterLayer,
            _config.TargetLayerMask,
            cell);

        _tracked.Add(new TrackedMonster(monster, cell));
        return true;
    }

    // A land table (no tier) may hold only land monsters, and a water table only creatures living in its tier.
    private static void RequireHabitat(MonsterCatalog catalog, IReadOnlyList<SpawnTableEntry> entries, string table, WaterDepthTier? tier)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            _ = catalog.TryGetIndex(entries[i].Monster, out ushort index);
            if (catalog.GetDefinition(index).IsBoss)
            {
                throw new ArgumentException($"Spawn table '{table}' lists the boss '{entries[i].Monster}', which rises only at its lair.");
            }

            WaterHabitat? habitat = catalog.GetDefinition(index).Habitat;
            bool fits = tier is { } waterTier ? habitat is { } h && h.Contains(waterTier) : habitat is null;
            if (!fits)
            {
                throw new ArgumentException($"Spawn table '{table}' lists '{entries[i].Monster}', which does not live there.");
            }
        }
    }

    private void ReapDead(WorldSimulation world, ulong tickIndex)
    {
        EntityManager entities = world.Entities;
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            TrackedMonster tracked = _tracked[i];
            bool alive = entities.IsAlive(tracked.Entity) &&
                         (!entities.HasHealth(tracked.Entity) || !entities.GetHealth(tracked.Entity).IsDead);
            if (alive)
            {
                continue;
            }

            ref CellState state = ref CollectionsMarshal.GetValueRefOrNullRef(_cells, tracked.Cell);
            if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref state))
            {
                state.Alive = Math.Max(0, state.Alive - 1);
                state.NextSpawnTick = Math.Max(state.NextSpawnTick, tickIndex + _config.RespawnDelayTicks);
            }

            _tracked[i] = _tracked[^1];
            _tracked.RemoveAt(_tracked.Count - 1);
        }
    }

    private struct CellState
    {
        public int Alive;
        public ulong NextSpawnTick;
        public uint Attempts;
    }

    private readonly record struct TrackedMonster(Entity Entity, CellCoord Cell);

    private sealed class SpawnTableData
    {
        private readonly ushort[] _monsters;
        private readonly float[] _dayWeights;
        private readonly float[] _nightWeights;
        private readonly int[] _minLevels;
        private readonly int[] _maxLevels;

        private SpawnTableData(ushort[] monsters, float[] dayWeights, float[] nightWeights, int[] minLevels, int[] maxLevels)
        {
            _monsters = monsters;
            _dayWeights = dayWeights;
            _nightWeights = nightWeights;
            _minLevels = minLevels;
            _maxLevels = maxLevels;
        }

        public static SpawnTableData Create(MonsterCatalog catalog, IReadOnlyList<SpawnTableEntry> entries, string name)
        {
            int count = entries.Count;
            ushort[] monsters = new ushort[count];
            float[] day = new float[count];
            float[] night = new float[count];
            int[] minLevels = new int[count];
            int[] maxLevels = new int[count];
            for (int i = 0; i < count; i++)
            {
                SpawnTableEntry entry = entries[i];
                if (!catalog.TryGetIndex(entry.Monster, out monsters[i]))
                {
                    throw new ArgumentException($"Spawn table '{name}' references unknown monster '{entry.Monster}'.", nameof(entries));
                }

                MonsterDefinition def = catalog.GetDefinition(monsters[i]);
                day[i] = entry.Weight;
                night[i] = entry.NightWeight ?? entry.Weight;
                minLevels[i] = def.MinLevel;
                maxLevels[i] = def.MaxLevel;
            }

            return new SpawnTableData(monsters, day, night, minLevels, maxLevels);
        }

        /// <summary>Weighted pick among entries whose monster level range contains <paramref name="level"/>.</summary>
        public bool TryPick(float roll, bool isNight, int level, out ushort monsterIndex)
        {
            float[] weights = isNight ? _nightWeights : _dayWeights;
            monsterIndex = 0;
            float total = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                if (this.IsEligible(i, level))
                {
                    total += weights[i];
                }
            }

            if (total <= 0f)
            {
                return false;
            }

            float threshold = roll * total;
            int last = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                if (!this.IsEligible(i, level) || weights[i] <= 0f)
                {
                    continue;
                }

                last = i;
                threshold -= weights[i];
                if (threshold < 0f)
                {
                    monsterIndex = _monsters[i];
                    return true;
                }
            }

            monsterIndex = _monsters[last];
            return true;
        }

        private bool IsEligible(int i, int level) => level >= _minLevels[i] && level <= _maxLevels[i];
    }
}
