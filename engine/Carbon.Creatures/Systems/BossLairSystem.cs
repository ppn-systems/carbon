// Copyright (c) 2026 PPN Systems. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Carbon.AI.Components;
using Carbon.Creatures.Components;
using Carbon.Creatures.Definitions;
using Carbon.Creatures.Scaling;
using Carbon.Creatures.Spawning;
using Carbon.Simulation.Clock;
using Carbon.Simulation.ECS;
using Carbon.Simulation.ECS.Systems;
using Carbon.Simulation.World;

namespace Carbon.Creatures.Systems;

/// <summary>
/// Raises the abyss boss at its lair (ADR-032 D15). Every <see cref="SpawningConfig.ScanIntervalTicks"/> it finds the
/// lairs within <see cref="BossLairConfig.ActivationRadius"/> of each <see cref="SpawnAnchorComponent"/> (players):
/// <list type="bullet">
/// <item>A lair without a living boss raises one when its respawn timer has run out. The boss's level and stats come
/// from its distance to the world origin, like any monster's.</item>
/// <item>When the boss dies, the lair waits <see cref="BossLairConfig.RespawnTicks"/>.</item>
/// <item>A boss left alone for <see cref="BossLairConfig.IdleDespawnTicks"/> sinks back and rises again at full health
/// when a player returns. It never sinks while a player in its chunk is fighting it: a blow or a wound within
/// <see cref="BossLairConfig.CombatGraceTicks"/> holds it.</item>
/// </list>
/// The boss is not a spawner monster: it takes no density-cell slot and is never picked from a spawn table. Lairs are
/// looked up once per cell and cached while a player's scan reaches the cell. A cell no scan reaches is dropped once it
/// holds nothing a fresh lookup would lose: no living boss and no running respawn timer (ALLOC-7). Runs in
/// <see cref="SimulationPhase.PostUpdate"/>.
/// </summary>
public sealed class BossLairSystem : ISimulationSystem
{
    private const int MaxAnchors = 64;

    private readonly MonsterCatalog _catalog;
    private readonly SpawningConfig _config;
    private readonly BossLairConfig _lairConfig;
    private readonly MonsterScaling _scaling;
    private readonly ILairSource _lairs;
    private readonly Vector2 _worldOrigin;
    private readonly ushort _bossIndex;
    private readonly Dictionary<(int X, int Y), Lair> _cells = new(16);

    /// <summary>Initializes a new instance of the <see cref="BossLairSystem"/> class.</summary>
    /// <param name="catalog">Monster catalog.</param>
    /// <param name="config">Spawning configuration with a <see cref="SpawningConfig.BossLairs"/> block.</param>
    /// <param name="scaling">Level and stat scaling shared with the spawner.</param>
    /// <param name="lairs">Where the lairs are.</param>
    /// <param name="worldOrigin">World origin; distance from it sets the boss's level.</param>
    /// <exception cref="ArgumentException">Thrown when the config has no boss, or names a monster that is not a boss.</exception>
    public BossLairSystem(MonsterCatalog catalog, SpawningConfig config, MonsterScaling scaling, ILairSource lairs, Vector2 worldOrigin)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(scaling);
        ArgumentNullException.ThrowIfNull(lairs);

        _lairConfig = config.BossLairs ?? throw new ArgumentException("spawning.json has no bossLairs block.", nameof(config));
        if (!catalog.TryGetIndex(_lairConfig.Monster, out _bossIndex) || !catalog.GetDefinition(_bossIndex).IsBoss)
        {
            throw new ArgumentException($"bossLairs.monster '{_lairConfig.Monster}' must be a monsters.json entry with isBoss.", nameof(config));
        }

        _catalog = catalog;
        _config = config;
        _scaling = scaling;
        _lairs = lairs;
        _worldOrigin = worldOrigin;
    }

    /// <inheritdoc/>
    public SimulationPhase Phase => SimulationPhase.PostUpdate;

    /// <summary>Gets the boss risen at the lair of a cell, or <see cref="Entity.Null"/>.</summary>
    /// <param name="cellX">Lair cell X.</param>
    /// <param name="cellY">Lair cell Y.</param>
    /// <returns>The living boss, or <see cref="Entity.Null"/>.</returns>
    public Entity GetBoss(int cellX, int cellY) => _cells.TryGetValue((cellX, cellY), out Lair lair) ? lair.Boss : Entity.Null;

    /// <summary>Gets the number of lair cells held: those near a player, plus lairs with a living boss or a running respawn timer.</summary>
    public int CellCount => _cells.Count;

    /// <inheritdoc/>
    public void Update(WorldSimulation world, FixedTickArgs tick)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (tick.TickIndex % _config.ScanIntervalTicks != 0)
        {
            return;
        }

        EntityManager entities = world.Entities;
        Span<Vector2> anchors = stackalloc Vector2[MaxAnchors];
        int anchorCount = CollectAnchors(entities, anchors);
        ulong now = tick.TickIndex;
        float radius = _lairConfig.ActivationRadius;
        float radiusSq = radius * radius;
        float cellSize = _lairs.CellSize;

        for (int a = 0; a < anchorCount; a++)
        {
            Vector2 p = anchors[a];
            int minX = (int)MathF.Floor((p.X - radius) / cellSize);
            int maxX = (int)MathF.Floor((p.X + radius) / cellSize);
            int minY = (int)MathF.Floor((p.Y - radius) / cellSize);
            int maxY = (int)MathF.Floor((p.Y + radius) / cellSize);
            for (int cy = minY; cy <= maxY; cy++)
            {
                for (int cx = minX; cx <= maxX; cx++)
                {
                    ref Lair lair = ref this.LairAt(cx, cy);
                    lair.ScanTick = now;
                    if (lair.Exists && Vector2.DistanceSquared(p, lair.Anchor) <= radiusSq)
                    {
                        lair.LastNearTick = now;
                        lair.Near = true;
                    }
                }
            }
        }

        foreach (KeyValuePair<(int X, int Y), Lair> pair in _cells)
        {
            ref Lair lair = ref CollectionsMarshal.GetValueRefOrNullRef(_cells, pair.Key);
            if (lair.Exists)
            {
                this.StepLair(world, ref lair, anchors[..anchorCount], now);
            }

            // Out of every scan, with no boss and no timer, the cell equals a fresh lookup: drop it. Removing the current
            // entry during the enumeration is allowed and keeps it valid.
            if (lair.ScanTick != now && !lair.Boss.IsValid && now >= lair.NextSpawnTick)
            {
                _ = _cells.Remove(pair.Key);
            }
        }
    }

    private static int CollectAnchors(EntityManager entities, Span<Vector2> anchors)
    {
        ComponentPool<SpawnAnchorComponent> pool = entities.GetPool<SpawnAnchorComponent>();
        ReadOnlySpan<int> slots = pool.EntitySlots;
        int count = 0;
        for (int i = 0; i < slots.Length && count < anchors.Length; i++)
        {
            Entity anchor = entities.GetEntity(slots[i]);
            if (anchor.IsValid && entities.HasTransform(anchor))
            {
                anchors[count++] = entities.GetTransform(anchor).Position;
            }
        }

        return count;
    }

    private ref Lair LairAt(int cellX, int cellY)
    {
        ref Lair lair = ref CollectionsMarshal.GetValueRefOrAddDefault(_cells, (cellX, cellY), out bool known);
        if (!known)
        {
            lair.Exists = _lairs.TryGetLair(cellX, cellY, out lair.Anchor);
        }

        return ref lair;
    }

    private void StepLair(WorldSimulation world, ref Lair lair, ReadOnlySpan<Vector2> anchors, ulong now)
    {
        EntityManager entities = world.Entities;
        bool near = lair.Near;
        lair.Near = false;

        if (lair.Boss.IsValid)
        {
            bool alive = entities.IsAlive(lair.Boss) && (!entities.HasHealth(lair.Boss) || !entities.GetHealth(lair.Boss).IsDead);
            if (!alive)
            {
                lair.Boss = Entity.Null;
                lair.NextSpawnTick = now + _lairConfig.RespawnTicks;
                return;
            }

            if (this.IsFoughtInItsChunk(entities, ref lair, anchors))
            {
                lair.LastEngagedTick = now;
            }

            if (now - lair.LastNearTick >= _lairConfig.IdleDespawnTicks && now - lair.LastEngagedTick >= _lairConfig.CombatGraceTicks)
            {
                world.DespawnEntity(lair.Boss);
                lair.Boss = Entity.Null;
                lair.NextSpawnTick = now; // rises again, whole, when a player comes back
            }

            return;
        }

        if (near && now >= lair.NextSpawnTick)
        {
            float distance = Vector2.Distance(lair.Anchor, _worldOrigin);
            ScaledMonsterStats stats = _scaling.Compute(distance, isNight: false, _catalog.GetDefinition(_bossIndex));
            lair.Boss = MonsterFactory.Spawn(world, _catalog, _bossIndex, lair.Anchor, in stats, _config.MonsterLayer, _config.TargetLayerMask);
            lair.LastHealth = stats.MaxHealth;
            lair.LastEngagedTick = 0UL;
        }
    }

    // A fight: the boss has a target or lost health since the last scan, and a player stands in its chunk.
    private bool IsFoughtInItsChunk(EntityManager entities, ref Lair lair, ReadOnlySpan<Vector2> anchors)
    {
        float health = entities.HasHealth(lair.Boss) ? entities.GetHealth(lair.Boss).Current : lair.LastHealth;
        bool wounded = health < lair.LastHealth;
        lair.LastHealth = health;
        bool targeting = entities.TryGetComponent(lair.Boss, out AiBrainComponent brain) && brain.TargetEntity.IsValid;
        if (!wounded && !targeting)
        {
            return false;
        }

        float chunk = _lairs.ChunkSize;
        Vector2 at = entities.GetTransform(lair.Boss).Position;
        int bx = (int)MathF.Floor(at.X / chunk);
        int by = (int)MathF.Floor(at.Y / chunk);
        for (int i = 0; i < anchors.Length; i++)
        {
            if ((int)MathF.Floor(anchors[i].X / chunk) == bx && (int)MathF.Floor(anchors[i].Y / chunk) == by)
            {
                return true;
            }
        }

        return false;
    }

    private struct Lair
    {
        public bool Exists;
        public Vector2 Anchor;
        public Entity Boss;
        public ulong NextSpawnTick;
        public ulong LastNearTick;
        public ulong LastEngagedTick;
        public float LastHealth;
        public bool Near;
        public ulong ScanTick;
    }
}
